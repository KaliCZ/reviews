using Microsoft.EntityFrameworkCore;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Entities;
using Reviews.Shared;
using StackExchange.Redis;
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
    // Page-1 cache key, shared with the API. The API populates it on a miss;
    // mutating workflows blow it away here so the next read rebuilds.
    private static string FirstPageKey(long productId) => $"reviews:product:{productId}:page:1";

    [Activity(ReviewActivityNames.PersistReview)]
    public async Task PersistAsync(SubmitReviewInput input)
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
    }

    [Activity(ReviewActivityNames.ApproveReview)]
    public async Task ApproveAsync(Guid reviewId)
    {
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
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
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
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
            .Select(r => new { r.AuthorId, r.ProductId, r.CreatedAt })
            .FirstOrDefaultAsync();

        if (row is null)
            return new ReviewLookupResult(Found: false, OwnedByAuthor: false, ProductId: 0, CreatedAt: default);

        return new ReviewLookupResult(true, row.AuthorId == authorId, row.ProductId, row.CreatedAt);
    }

    [Activity(ReviewActivityNames.ApplyReviewEdit)]
    public async Task ApplyEditAsync(EditReviewInput input)
    {
        var review = await db.Reviews
            .Where(r => r.Id == input.ReviewId
                     && r.AuthorId == input.AuthorId
                     && r.Status != ReviewStatus.Deleted)
            .FirstOrDefaultAsync();
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
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
        if (review is null) return;
        review.SoftDelete();
        await db.SaveChangesAsync();
        logger.LogInformation("Soft-deleted review {ReviewId}", reviewId);
    }

    [Activity(ReviewActivityNames.UpsertVote)]
    public async Task<long?> UpsertVoteAsync(VoteInput input)
    {
        // Resolve product_id and confirm the review is live in the same shot.
        // Pinning to Approved means votes can't accrue on deleted/rejected/
        // pending reviews even if a stale client tried.
        var productId = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == input.ReviewId && r.Status == ReviewStatus.Approved)
            .Select(r => (long?)r.ProductId)
            .FirstOrDefaultAsync();
        if (productId is null) return null;

        await using var tx = await db.Database.BeginTransactionAsync();

        // EF Core's tracking semantics make the upsert verbose; the raw
        // INSERT … ON CONFLICT … is clearer for a single-statement upsert
        // and keeps the round-trip count down.
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO reviews.review_votes (""ReviewId"", ""VoterId"", ""Value"", ""CreatedAt"")
            VALUES ({input.ReviewId}, {input.VoterId}, {input.Value}, NOW())
            ON CONFLICT (""ReviewId"", ""VoterId"")
            DO UPDATE SET ""Value"" = EXCLUDED.""Value"", ""CreatedAt"" = NOW()");

        // Recompute denormalized score from the source-of-truth vote rows so
        // it self-heals on every vote (any drift is corrected within a tick).
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE reviews.reviews
               SET ""Score"" = COALESCE((
                   SELECT SUM(""Value"") FROM reviews.review_votes WHERE ""ReviewId"" = {input.ReviewId}
               ), 0),
                   ""UpdatedAt"" = NOW()
             WHERE ""Id"" = {input.ReviewId}");

        await tx.CommitAsync();
        logger.LogInformation(
            "Upserted vote ({Value}) by {Voter} on review {Review} (product {Product})",
            input.Value, input.VoterId, input.ReviewId, productId);
        return productId;
    }

    [Activity(ReviewActivityNames.RefreshFirstPageCache)]
    public async Task RefreshFirstPageCacheAsync(long productId)
    {
        // Invalidate-and-let-next-read-rebuild beats compute-and-write here:
        // the workflow doesn't have to know the cached payload's exact shape,
        // and a cache miss on the next request is the cheapest possible repair.
        var deleted = await redis.GetDatabase().KeyDeleteAsync(FirstPageKey(productId));
        logger.LogInformation(
            "Invalidated first-page cache for product {ProductId} (existed: {Existed})",
            productId, deleted);
    }
}
