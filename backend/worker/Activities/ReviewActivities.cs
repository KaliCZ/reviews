using Microsoft.EntityFrameworkCore;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Caching;
using Reviews.Infrastructure.Entities;
using Reviews.Shared;
using StrongTypes;
using Temporalio.Activities;

namespace Reviews.Worker;

public class ReviewActivities(
    ReviewsDbContext db,
    IReviewCacheInvalidator cacheInvalidator,
    ILogger<ReviewActivities> logger)
{
    [Activity(ReviewActivityNames.PersistReview)]
    public async Task<string> PersistAsync(SubmitReviewInput input)
    {
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

        // Slug returned so the workflow's cache-invalidation activity doesn't
        // need a second lookup.
        return await db.Products
            .AsNoTracking()
            .Where(p => p.Id == input.ProductId)
            .Select(p => p.Slug.Value)
            .SingleAsync();
    }

    [Activity(ReviewActivityNames.ApproveReview)]
    public async Task ApproveAsync(Guid reviewId)
    {
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

    // No advisory lock: concurrent recomputes converge because both writers
    // read the same committed source-of-truth rows.
    [Activity(ReviewActivityNames.RecomputeProductRating)]
    public async Task RecomputeProductRatingAsync(long productId)
    {
        await db.Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.ReviewCount, p => p.Reviews
                    .Count(r => r.Status == ReviewStatus.Approved))
                .SetProperty(p => p.AverageRating, p => p.Reviews
                    .Where(r => r.Status == ReviewStatus.Approved)
                    .Average(r => (double?)(short)r.Rating) ?? 0));
        logger.LogInformation("Recomputed denormalized rating for product {ProductId}", productId);
    }

    [Activity(ReviewActivityNames.InvalidateProductCaches)]
    public Task InvalidateProductCachesAsync(string productSlug) =>
        cacheInvalidator.InvalidateProductAsync(productSlug);
}
