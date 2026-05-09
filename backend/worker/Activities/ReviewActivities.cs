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
// activity returns. SaveChangesAsync wraps each unit of work in a single
// transaction; no manual BeginTransactionAsync needed.
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
            imageUrls:  input.ImageUrls);
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
            .Select(r => new { r.AuthorId, r.ProductId, ProductSlug = r.Product.Slug.Value, r.CreatedAtUtc })
            .SingleOrDefaultAsync();

        if (row is null)
            return new ReviewLookupResult(Found: false, OwnedByAuthor: false, ProductId: 0, ProductSlug: string.Empty, CreatedAtUtc: default);

        return new ReviewLookupResult(true, row.AuthorId == authorId, row.ProductId, row.ProductSlug, row.CreatedAtUtc);
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

        review.ApplyEdit(input.Rating, input.Title, input.Body, input.ImageUrls);
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

    [Activity(ReviewActivityNames.RecordVote)]
    public async Task<VoteResult> RecordVoteAsync(VoteInput input)
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

        // Try to update the existing (review, voter) row in one statement;
        // if no row matched, INSERT. Safe to be the only writer per pair
        // because the workflow id collapses concurrent same-user votes
        // (IdConflictPolicy.UseExisting joins the in-flight execution).
        await using var tx = await db.Database.BeginTransactionAsync();

        var updated = await db.ReviewVotes
            .Where(v => v.ReviewId == input.ReviewId && v.VoterId == input.VoterId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsUpvote, input.IsUpvote));

        if (updated == 0)
        {
            db.ReviewVotes.Add(new ReviewVote(input.ReviewId, input.VoterId, input.IsUpvote));
            await db.SaveChangesAsync();
        }

        // Recompute denormalized Review.Score from source-of-truth vote rows
        // so it self-heals on every vote (any drift corrects within a tick).
        // Single UPDATE … SET Score = (subquery) — no entity load.
        await db.Reviews
            .Where(r => r.Id == input.ReviewId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Score, _ => db.ReviewVotes
                    .Where(v => v.ReviewId == input.ReviewId)
                    .Sum(v => v.IsUpvote ? 1 : -1))
                .SetProperty(r => r.UpdatedAtUtc, _ => DateTime.UtcNow));

        await tx.CommitAsync();
        logger.LogInformation(
            "Recorded vote (upvote={IsUpvote}) by {Voter} on review {Review} (product {Slug})",
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
