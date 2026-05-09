using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reviews.Api.Models;
using Reviews.Api.Services;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Entities;
// ReviewsCacheKeys lives in Reviews.Infrastructure (shared with the worker).
using StackExchange.Redis;
using StrongTypes;

namespace Reviews.Api.Controllers;

// Read paths for products and their reviews. Public — anonymous browsing is
// the default. Authoring/voting endpoints live on ReviewsController and
// require [Authorize].
//
// Caching strategy:
//   - The product list, the product detail (without per-viewer fields), and
//     the first page of reviews under default sort/no-filter all live in
//     Redis. Per-viewer fields (MyVote, Mine, MyReviewId) are stripped before
//     caching and re-enriched on read.
//   - Workflow activities invalidate ReviewsCacheKeys.AffectedBy(productId)
//     after every write — that single helper is the only place caller-side
//     code needs to know which keys cover which surfaces.
//   - Cache-first ordering: keyed lookups (list, slug detail, first page of
//     reviews) check Redis BEFORE hitting Postgres. The previous "look up
//     product, then check cache" defeated the cache for the most common
//     requests.
[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class ProductsController(
    ReviewsDbContext db,
    IConnectionMultiplexer redis,
    ICurrentUser currentUser) : ControllerBase
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private static readonly TimeSpan ListCacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DetailCacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan FirstPageCacheTtl = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // GET /api/products — list of all products with summary stats. Cached
    // for 15 min and invalidated on every review mutation; the per-product
    // average/count are denormalized into the cached payload, so a cold
    // catalog page is cheap regardless of review-table size.
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductSummary>>> GetAll(CancellationToken ct)
    {
        var cache = redis.GetDatabase();
        var cached = await cache.StringGetAsync(ReviewsCacheKeys.ProductList);
        if (cached.HasValue)
            return Ok(JsonSerializer.Deserialize<List<ProductSummary>>((string)cached!, Json)!);

        var rows = await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new ProductSummary
            {
                Id = p.Id,
                Slug = p.Slug,
                Name = p.Name,
                ImageUrl = p.ImageUrl,
                AverageRating = p.Reviews.Where(r => r.Status == ReviewStatus.Approved).Average(r => (double?)(short)r.Rating) ?? 0,
                ReviewCount = p.Reviews.Count(r => r.Status == ReviewStatus.Approved),
            })
            .ToListAsync(ct);

        await cache.StringSetAsync(
            ReviewsCacheKeys.ProductList,
            JsonSerializer.Serialize(rows, Json),
            ListCacheTtl);
        return Ok(rows);
    }

    // GET /api/products/{slug} — product detail keyed by URL-safe slug. Also
    // returns the current viewer's existing review id (if any) so the SPA
    // can switch a "Write a review" CTA to "Edit your review" without an
    // extra round-trip. The non-personalized projection is cached; MyReviewId
    // is computed at read time per viewer.
    [HttpGet("{slug}")]
    public async Task<ActionResult<ProductDetail>> GetBySlug(NonEmptyString slug, CancellationToken ct)
    {
        var cache = redis.GetDatabase();
        var cacheKey = ReviewsCacheKeys.ProductDetail(slug.Value);

        ProductDetail? detail;
        var cached = await cache.StringGetAsync(cacheKey);
        if (cached.HasValue)
        {
            detail = JsonSerializer.Deserialize<ProductDetail>((string)cached!, Json)!;
        }
        else
        {
            // SingleOrDefault: slug has a unique index — at most one row matches,
            // and a duplicate would be a real bug worth surfacing.
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
                    Avg = p.Reviews.Where(r => r.Status == ReviewStatus.Approved).Average(r => (double?)(short)r.Rating) ?? 0,
                    Count = p.Reviews.Count(r => r.Status == ReviewStatus.Approved)
                })
                .SingleOrDefaultAsync(ct);
            if (p is null) return NotFound();

            detail = new ProductDetail
            {
                Id = p.Id,
                Slug = p.Slug,
                Name = p.Name,
                Description = p.Description,
                ImageUrl = p.ImageUrl,
                AverageRating = p.Avg,
                ReviewCount = p.Count,
                MyReviewId = null,
            };

            await cache.StringSetAsync(
                cacheKey,
                JsonSerializer.Serialize(detail, Json),
                DetailCacheTtl);
        }

        // Personalize after the cache lookup. SingleOrDefault: the partial
        // unique index `uq_reviews_product_author` enforces 0..1 live review
        // per (product, author).
        Guid? myReviewId = null;
        if (currentUser.User is { } user)
        {
            myReviewId = await db.Reviews
                .AsNoTracking()
                .Where(r => r.ProductId == detail.Id && r.AuthorId == user.Id && r.Status != ReviewStatus.Deleted)
                .Select(r => (Guid?)r.Id)
                .SingleOrDefaultAsync(ct);
        }

        return Ok(detail with { MyReviewId = myReviewId });
    }

    // GET /api/products/{slug}/reviews?sort=&rating=&hasPhotos=&page=&pageSize=
    //   - `sort` is the ReviewSort enum, serialized as a string.
    //   - `rating` is repeatable for multi-select (e.g. ?rating=4&rating=5).
    //   - `page` is 1-based; offset pagination so users can skip directly to
    //     a numbered page (better UX for reviews than opaque cursors).
    //   - First page of `sort=newest` with no filters is cached in Redis.
    [HttpGet("{slug}/reviews")]
    public async Task<ActionResult<ReviewsPage>> GetReviews(
        NonEmptyString slug,
        [FromQuery] ReviewSort sort = ReviewSort.Newest,
        [FromQuery(Name = "rating")] int[]? ratings = null,
        [FromQuery] bool? hasPhotos = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var cache = redis.GetDatabase();
        var ratingSet = NormalizeRatings(ratings);
        var isFirstPage = page == 1
            && pageSize == DefaultPageSize
            && ratingSet is null
            && hasPhotos is null
            && sort == ReviewSort.Newest;

        // Cache lookup BEFORE the product existence check — we cache the
        // 404 absence below (cached productId is null) too, so a slug-typo
        // attacker can't keep beating on Postgres for missing rows.
        if (isFirstPage)
        {
            var cached = await cache.StringGetAsync(ReviewsCacheKeys.FirstPage(slug.Value));
            if (cached.HasValue)
            {
                var page1 = JsonSerializer.Deserialize<ReviewsPage>((string)cached!, Json)!;
                return Ok(await EnrichForViewerAsync(page1, ct));
            }
        }

        var product = await db.Products
            .AsNoTracking()
            .Where(p => p.Slug == slug)
            .Select(p => new { p.Id })
            .SingleOrDefaultAsync(ct);
        if (product is null) return NotFound();

        var built = await BuildPageAsync(product.Id, sort, ratingSet, hasPhotos, page, pageSize, ct);

        if (isFirstPage)
        {
            // Cache the un-personalised payload — MyVote and Mine are
            // per-user and don't belong in the shared cache.
            var toCache = built with
            {
                Items = built.Items
                    .Select(i => i with { MyVote = null, Mine = false })
                    .ToList()
            };
            await cache.StringSetAsync(
                ReviewsCacheKeys.FirstPage(slug.Value),
                JsonSerializer.Serialize(toCache, Json),
                FirstPageCacheTtl);
        }

        return Ok(await EnrichForViewerAsync(built, ct));
    }

    // Single pipeline:
    //   1. base query with all filters applied,
    //   2. order by the chosen sort key (always carrying an Id tiebreaker),
    //   3. ApplyOffsetPagination paginates anything sorted.
    // The previous helpful-mode cursor branch was a special case; now every
    // sort goes through the same path and the difference between sorts is
    // just the ORDER BY clause.
    private async Task<ReviewsPage> BuildPageAsync(
        long productId,
        ReviewSort sort,
        HashSet<short>? ratings,
        bool? hasPhotos,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        IQueryable<Review> q = db.Reviews
            .AsNoTracking()
            .Where(r => r.ProductId == productId && r.Status == ReviewStatus.Approved);

        if (ratings is { Count: > 0 }) q = q.Where(r => ratings.Contains((short)r.Rating));
        if (hasPhotos is true)         q = q.Where(r => r.ImageUrls.Count > 0);

        var ordered = sort switch
        {
            ReviewSort.Helpful => q.OrderByDescending(r => r.Score).ThenByDescending(r => r.Id),
            ReviewSort.Highest => q.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            ReviewSort.Lowest  => q.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
            _ /* Newest */     => q.OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id),
        };

        var total = await q.CountAsync(ct);
        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var uid = currentUser.User?.Id;
        var items = rows.Select(r => new ReviewItem
        {
            Id = r.Id,
            ProductId = r.ProductId,
            AuthorId = r.AuthorId,
            AuthorName = r.AuthorName,
            Rating = r.Rating,
            Title = r.Title,
            Body = r.Body,
            // Each persisted URL is required to be non-empty by the DTO contract;
            // wrap on the way out so the SPA's generated client sees the same
            // shape (`string` with minLength 1).
            ImageUrls = r.ImageUrls.Select(u => u.ToNonEmpty()).ToList(),
            Language = r.Language,
            Score = r.Score,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            MyVote = null,
            Mine = uid is not null && r.AuthorId == uid,
        }).ToList();

        return new ReviewsPage
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };
    }

    // Adds the per-viewer fields (MyVote, Mine) onto a page just read from
    // either the DB or the cache. One indexed query for the votes; Mine is
    // a free Guid comparison after that.
    private async Task<ReviewsPage> EnrichForViewerAsync(ReviewsPage page, CancellationToken ct)
    {
        if (currentUser.User is not { } user || page.Items.Count == 0) return page;

        var ids = page.Items.Select(i => i.Id).ToArray();
        var votes = await db.ReviewVotes
            .AsNoTracking()
            .Where(v => v.VoterId == user.Id && ids.Contains(v.ReviewId))
            .ToDictionaryAsync(v => v.ReviewId, v => v.IsUpvote, ct);

        return page with
        {
            Items = page.Items
                .Select(i => i with
                {
                    MyVote = votes.TryGetValue(i.Id, out var v) ? v : null,
                    Mine = i.AuthorId == user.Id,
                })
                .ToList()
        };
    }

    private static HashSet<short>? NormalizeRatings(int[]? raw)
    {
        if (raw is null || raw.Length == 0) return null;
        var set = new HashSet<short>();
        foreach (var v in raw)
            if (v is >= 1 and <= 5)
                set.Add((short)v);
        return set.Count == 0 ? null : set;
    }
}
