using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Reviews.Infrastructure.Caching;

// Single shared invalidator for both the vote controller (sync request path)
// and the submit/edit/delete workflow activities. Retries swallow transient
// Redis blips: the 24h TTL on every cached entry plus the next mutation
// re-DEL'ing the same keys means a permanent miss isn't catastrophic, only
// stale until either backstop fires.
public interface IReviewCacheInvalidator
{
    Task InvalidateProductAsync(string productSlug, CancellationToken ct = default);
}

public sealed class ReviewCacheInvalidator(
    IConnectionMultiplexer redis,
    ILogger<ReviewCacheInvalidator> logger) : IReviewCacheInvalidator
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(80),
        TimeSpan.FromMilliseconds(200),
    ];

    public async Task InvalidateProductAsync(string productSlug, CancellationToken ct = default)
    {
        var keys = ReviewsCacheKeys.AffectedBy(productSlug)
            .Select(k => (RedisKey)k)
            .ToArray();

        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var deleted = await redis.GetDatabase().KeyDeleteAsync(keys);
                logger.LogInformation(
                    "Invalidated {Count}/{Total} cache keys for product {Slug}",
                    deleted, keys.Length, productSlug);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length)
            {
                logger.LogWarning(ex,
                    "Cache invalidation for {Slug} failed (attempt {Attempt}); retrying",
                    productSlug, attempt + 1);
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Cache invalidation for {Slug} failed after {Attempts} attempts; relying on TTL + next-mutation re-DEL",
                    productSlug, RetryDelays.Length + 1);
                return;
            }
        }
    }
}
