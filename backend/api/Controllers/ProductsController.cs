using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reviews.Api.Models;
using Reviews.Api.Services;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Entities;
using StackExchange.Redis;

namespace Reviews.Api.Controllers;

// Read paths for products and their reviews. Public — anonymous browsing is
// the default. Authoring/voting endpoints live on ReviewsController and
// require [Authorize].
[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class ProductsController(
    ReviewsDbContext db,
    IConnectionMultiplexer redis,
    ICurrentUser currentUser) : ControllerBase
{
    private const int PageSize = 20;
    private static readonly TimeSpan FirstPageCacheTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string FirstPageKey(long productId) => $"reviews:product:{productId}:page:1";

    // GET /api/products — list of all products with summary stats. Cheap
    // enough to compute on the fly for 10 seeded products; if the catalog
    // grows, denormalize avg_rating / review_count onto the products row.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductSummary>>> GetAll(CancellationToken ct)
    {
        var rows = await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new ProductSummary(
                p.Id,
                p.Slug,
                p.Name,
                p.ImageUrl,
                p.Reviews.Where(r => r.Status == ReviewStatus.Approved).Average(r => (double?)r.Rating) ?? 0,
                p.Reviews.Count(r => r.Status == ReviewStatus.Approved)))
            .ToListAsync(ct);
        return Ok(rows);
    }

    // GET /api/products/{slug} — product detail keyed by URL-safe slug. Also
    // returns the current viewer's existing review id (if any) so the SPA
    // can switch a "Write a review" CTA to "Edit your review" without an
    // extra round-trip. The lookup is a single indexed PK-style query —
    // cheap, no point caching.
    [HttpGet("{slug}")]
    public async Task<ActionResult<ProductDetail>> GetBySlug(string slug, CancellationToken ct)
    {
        var p = await db.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => new
            {
                p.Id,
                p.Slug,
                p.Name,
                p.Description,
                p.ImageUrl,
                Avg = p.Reviews.Where(r => r.Status == ReviewStatus.Approved).Average(r => (double?)r.Rating) ?? 0,
                Count = p.Reviews.Count(r => r.Status == ReviewStatus.Approved)
            })
            .FirstOrDefaultAsync(ct);
        if (p is null) return NotFound();

        Guid? myReviewId = null;
        if (currentUser.Id is { } uid)
        {
            myReviewId = await db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId == p.Id && r.AuthorId == uid && r.Status != ReviewStatus.Deleted)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);
        }

        return Ok(new ProductDetail(p.Id, p.Slug, p.Name, p.Description, p.ImageUrl, p.Avg, p.Count, myReviewId));
    }

    // GET /api/products/{slug}/reviews?sort=&rating=&hasPhotos=&cursor=
    //   default sort/no filters/no cursor → first page, served from Redis cache
    //   anything else → straight to Postgres via keyset pagination
    [HttpGet("{slug}/reviews")]
    public async Task<ActionResult<ReviewsPage>> GetReviews(
        string slug,
        [FromQuery] string sort = "newest",
        [FromQuery] short? rating = null,
        [FromQuery] bool? hasPhotos = null,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct);
        if (product is null) return NotFound();

        var isFirstPage = cursor is null && rating is null && hasPhotos is null && sort == "newest";
        var cache = redis.GetDatabase();

        if (isFirstPage)
        {
            var cached = await cache.StringGetAsync(FirstPageKey(product.Id));
            if (cached.HasValue)
            {
                var page = JsonSerializer.Deserialize<ReviewsPage>((string)cached!, Json)!;
                page = await EnrichForViewerAsync(page, ct);
                return Ok(page);
            }
        }

        var built = await BuildPageAsync(product.Id, sort, rating, hasPhotos, cursor, ct);

        if (isFirstPage)
        {
            // Cache the un-personalised payload — MyVote and Mine are
            // per-user and don't belong in the shared cache. Enrichment is
            // a single PK lookup and runs after a cache hit too.
            var toCache = built with
            {
                Items = built.Items.Select(i => i with { MyVote = null, Mine = false }).ToList()
            };
            await cache.StringSetAsync(
                FirstPageKey(product.Id),
                JsonSerializer.Serialize(toCache, Json),
                FirstPageCacheTtl);
        }

        var enriched = await EnrichForViewerAsync(built, ct);
        return Ok(enriched);
    }

    private async Task<ReviewsPage> BuildPageAsync(
        long productId, string sort, short? rating, bool? hasPhotos, string? cursor, CancellationToken ct)
    {
        var c = ReviewCursor.TryDecode(cursor);

        var q = db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved);

        if (rating is short val) q = q.Where(r => r.Rating == val);
        if (hasPhotos is true)   q = q.Where(r => r.ImageUrls.Count > 0);

        // Each branch orders by the sort key + a (CreatedAt, Id) tiebreaker
        // so cursor pagination stays deterministic across writes.
        q = sort switch
        {
            "helpful" => c is null
                ? q.OrderByDescending(r => r.Score).ThenByDescending(r => r.Id)
                : q.Where(r => r.Score < c.Score || (r.Score == c.Score && r.Id.CompareTo(c.Id) < 0))
                   .OrderByDescending(r => r.Score).ThenByDescending(r => r.Id),
            "highest" => c is null
                ? q.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                : q.Where(r => r.Rating < c.Rating
                            || (r.Rating == c.Rating && r.CreatedAt < c.CreatedAt)
                            || (r.Rating == c.Rating && r.CreatedAt == c.CreatedAt && r.Id.CompareTo(c.Id) < 0))
                   .OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            "lowest" => c is null
                ? q.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                : q.Where(r => r.Rating > c.Rating
                            || (r.Rating == c.Rating && r.CreatedAt < c.CreatedAt)
                            || (r.Rating == c.Rating && r.CreatedAt == c.CreatedAt && r.Id.CompareTo(c.Id) < 0))
                   .OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            _ /* newest */ => c is null
                ? q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
                : q.Where(r => r.CreatedAt < c.CreatedAt
                            || (r.CreatedAt == c.CreatedAt && r.Id.CompareTo(c.Id) < 0))
                   .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
        };

        // Fetch one extra to determine "has more" without a COUNT(*) round-trip.
        var rows = await q.Take(PageSize + 1).ToListAsync(ct);
        var hasMore = rows.Count > PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var uid = currentUser.Id;
        var items = rows.Select(r => new ReviewItem(
            r.Id, r.ProductId, r.AuthorId, r.AuthorName, r.Rating, r.Title, r.Body,
            r.ImageUrls, r.Score, r.CreatedAt, r.UpdatedAt,
            MyVote: null,
            Mine: uid is not null && r.AuthorId == uid)).ToList();

        string? nextCursor = null;
        if (hasMore)
        {
            var last = rows[^1];
            nextCursor = new ReviewCursor(last.CreatedAt, last.Score, last.Rating, last.Id).Encode();
        }

        return new ReviewsPage(items, nextCursor);
    }

    // Adds the per-viewer fields (MyVote, Mine) onto a page just read from
    // either the DB or the cache. One indexed query for the votes; Mine is
    // a free Guid comparison after that.
    private async Task<ReviewsPage> EnrichForViewerAsync(ReviewsPage page, CancellationToken ct)
    {
        if (currentUser.Id is not { } uid || page.Items.Count == 0) return page;

        var ids = page.Items.Select(i => i.Id).ToArray();
        var votes = await db.ReviewVotes
            .AsNoTracking()
            .Where(v => v.VoterId == uid && ids.Contains(v.ReviewId))
            .ToDictionaryAsync(v => v.ReviewId, v => v.Value, ct);

        var enriched = page.Items
            .Select(i =>
            {
                short? myVote = votes.TryGetValue(i.Id, out var v) ? v : null;
                return i with { MyVote = myVote, Mine = i.AuthorId == uid };
            })
            .ToList();
        return new ReviewsPage(enriched, page.NextCursor);
    }
}
