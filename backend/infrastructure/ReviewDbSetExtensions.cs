using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Internal;
using Reviews.Infrastructure.Entities;

namespace Reviews.Infrastructure;

public static class ReviewDbSetExtensions
{
    public static Task RecomputeScoreAsync(
        this DbSet<Review> reviews, Guid reviewId, CancellationToken ct)
    {
        var votes = reviews.GetService<ICurrentDbContext>().Context.Set<ReviewVote>();
        return reviews
            .Where(r => r.Id == reviewId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Score, _ => votes
                    .Where(v => v.ReviewId == reviewId)
                    .Sum(v => v.IsUpvote ? 1 : -1))
                .SetProperty(r => r.UpdatedAtUtc, _ => DateTime.UtcNow), ct);
    }

    public static Task RecomputeAggregatesAsync(
        this IQueryable<Product> products, CancellationToken ct = default) =>
        products.ExecuteUpdateAsync(s => s
            .SetProperty(p => p.ReviewCount, p => p.Reviews
                .Count(r => r.Status == ReviewStatus.Approved))
            .SetProperty(p => p.AverageRating, p => p.Reviews
                .Where(r => r.Status == ReviewStatus.Approved)
                .Average(r => (double?)(short)r.Rating) ?? 0), ct);
}
