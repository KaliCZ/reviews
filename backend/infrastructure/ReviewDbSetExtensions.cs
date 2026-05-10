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
}
