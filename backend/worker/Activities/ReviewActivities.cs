using Microsoft.EntityFrameworkCore;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Entities;
using Reviews.Shared;
using StackExchange.Redis;
using StrongTypes;
using Temporalio.Activities;

namespace Reviews.Worker;

// Each activity owns a small durable step of the review workflows in
// shared/Workflows. Errors propagate back to Temporal which retries with
// exponential backoff per its activity-default policy.
//
// Activities are scoped — one DbContext per invocation, disposed when the
// activity returns. UpsertVote uses an explicit transaction since it does
// multi-step writes that must commit together.
public class ReviewActivities(
    ReviewsDbContext db,
    IConnectionMultiplexer redis,
    ILogger<ReviewActivities> logger)
{
    [Activity(ReviewActivityNames.PersistReview)]
    public async Task<string> PersistAsync(SubmitReviewInput input)
    {
        // Constructor enforces the rating/body invariants; status defaults to
        // Pending. The Approve activity flips it later in the workflow.
        var review = new Review(
            id:         input.ReviewId,
            productId:  input.ProductId,
            authorId:   input.AuthorId,
            authorName: input.AuthorName,
            rating:     input.Rating,
            title:      input.Title,
            body:       input.Body,
            imageUrls:  input.ImageUrls,
            language:   input.Language);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        logger.LogInformation(
            "Persisted review {ReviewId} (Pending) for product {ProductId}",
            input.ReviewId, input.ProductId);

        // Resolve the slug once and hand it back so the workflow can address
        // its cache-invalidation activity by slug without a second lookup.
        // SingleAsync: PK lookup, exactly one row exists.
        return await db.Products
            .AsNoTracking()
            .Where(p => p.Id == input.ProductId)
            .Select(p => p.Slug.Value)
            .SingleAsync();
    }

    [Activity(ReviewActivityNames.ApproveReview)]
    public async Task ApproveAsync(Guid reviewId)
    {
        // SingleOrDefault: PK lookup; either the row exists or it doesn't.
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
        {
            logger.LogWarning("Approve: review {ReviewId} not found", reviewId);
            return;
        }
        review.Approve();
        await db.SaveChangesAsync();
        logger.LogInformation("Approved review {ReviewId}", reviewId);
    }

    [Activity(ReviewActivityNames.RejectReview)]
    public async Task RejectAsync(Guid reviewId)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId);
        if (review is null)
        {
            logger.LogWarning("Reject: review {ReviewId} not found", reviewId);
            return;
        }
        review.Reject();
        await db.SaveChangesAsync();
        logger.LogInformation("Rejected review {ReviewId}", reviewId);
    }

    [Activity(ReviewActivityNames.LookupReview)]
    public async Task<ReviewLookupResult> LookupAsync(Guid reviewId, Guid authorId)
    {
        var row = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == reviewId && r.Status != ReviewStatus.Deleted)
            .Select(r => new { r.AuthorId, r.ProductId, ProductSlug = r.Product.Slug.Value, r.CreatedAt })
            .SingleOrDefaultAsync();

        if (row is null)
            return new ReviewLookupResult(Found: false, OwnedByAuthor: false, ProductId: 0, ProductSlug: string.Empty, CreatedAt: default);

        return new ReviewLookupResult(true, row.AuthorId == authorId, row.ProductId, row.ProductSlug, row.CreatedAt);
    }

    [Activity(ReviewActivityNames.ApplyReviewEdit)]
    public async Task ApplyEditAsync(EditReviewInput input)
    {
        // SingleOrDefault — composite filter on PK + AuthorId + alive status
        // narrows to at most one row.
        var review = await db.Reviews
            .Where(r => r.Id == input.ReviewId
                     && r.AuthorId == input.AuthorId
                     && r.Status != ReviewStatus.Deleted)
            .SingleOrDefaultAsync();
        if (review is null)
        {
            logger.LogWarning("ApplyEdit: review {ReviewId} not found / not owned by {Author}", input.ReviewId, input.AuthorId);
            return;
        }

        review.ApplyEdit(input.Rating, input.Title, input.Body, input.ImageUrls, input.Language);
        await db.SaveChangesAsync();
        logger.LogInformation("Applied edit to review {ReviewId}", input.ReviewId);
    }

    [Activity(ReviewActivityNames.SoftDeleteReview)]
    public async Task SoftDeleteAsync(Guid reviewId)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId);
        if (review is null) return;
        review.SoftDelete();
        await db.SaveChangesAsync();
        logger.LogInformation("Soft-deleted review {ReviewId}", reviewId);
    }

    [Activity(ReviewActivityNames.UpsertVote)]
    public async Task<VoteResult> UpsertVoteAsync(VoteInput input)
    {
        // Resolve product slug and confirm the review is live in the same shot.
        // Pinning to Approved means votes can't accrue on deleted/rejected/
        // pending reviews even if a stale client tried.
        var slug = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == input.ReviewId && r.Status == ReviewStatus.Approved)
            .Select(r => (string?)r.Product.Slug.Value)
            .SingleOrDefaultAsync();
        if (slug is null) return new VoteResult(false, string.Empty);

        await using var tx = await db.Database.BeginTransactionAsync();

        // EF Core's tracking semantics make the upsert verbose; the raw
        // INSERT … ON CONFLICT … is clearer for a single-statement upsert
        // and keeps the round-trip count down.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO reviews.review_votes (""ReviewId"", ""VoterId"", ""IsUpvote"", ""CreatedAt"")
            VALUES ({input.ReviewId}, {input.VoterId}, {input.IsUpvote}, NOW())
            ON CONFLICT (""ReviewId"", ""VoterId"")
            DO UPDATE SET ""IsUpvote"" = EXCLUDED.""IsUpvote"", ""CreatedAt"" = NOW()");

        // Recompute denormalized score from the source-of-truth vote rows so
        // it self-heals on every vote (any drift is corrected within a tick).
        // Each vote contributes +1 (upvote) or -1 (downvote).
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE reviews.reviews
               SET ""Score"" = COALESCE((
                   SELECT SUM(CASE WHEN ""IsUpvote"" THEN 1 ELSE -1 END)
                     FROM reviews.review_votes
                    WHERE ""ReviewId"" = {input.ReviewId}
               ), 0),
                   ""UpdatedAt"" = NOW()
             WHERE ""Id"" = {input.ReviewId}");

        await tx.CommitAsync();
        logger.LogInformation(
            "Upserted vote (upvote={IsUpvote}) by {Voter} on review {Review} (product {Slug})",
            input.IsUpvote, input.VoterId, input.ReviewId, slug);
        return new VoteResult(true, slug);
    }

    [Activity(ReviewActivityNames.InvalidateProductCaches)]
    public async Task InvalidateProductCachesAsync(string productSlug)
    {
        // Invalidate-and-let-next-read-rebuild beats compute-and-write here:
        // the workflow doesn't have to know the cached payloads' shapes, and
        // a cache miss on the next request is the cheapest possible repair.
        var keys = ReviewsCacheKeys.AffectedBy(productSlug)
            .Select(k => (RedisKey)k)
            .ToArray();
        var deleted = await redis.GetDatabase().KeyDeleteAsync(keys);
        logger.LogInformation(
            "Invalidated {Count}/{Total} cache keys for product {Slug}",
            deleted, keys.Length, productSlug);
    }
}
