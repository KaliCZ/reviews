using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Reviews.Api.Models;
using Reviews.Api.Services;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Entities;
using StackExchange.Redis;
using StrongTypes;

namespace Reviews.Api.Controllers;

// Read paths for products and reviews. Cached payloads strip per-viewer
// fields (MyVote, Mine, MyReviewId) and the controller re-enriches per
// request. Cache lookups happen BEFORE the product existence check so a
// slug-typo flood can't bypass Redis.
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
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

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

        // SingleOrDefault: partial unique index `uq_reviews_product_author`
        // enforces 0..1 live review per (product, author).
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

    // First page of sort=helpful with no filters is cached in Redis.
    [HttpGet("{slug}/reviews")]
    public async Task<ActionResult<ReviewsPage>> GetReviews(
        NonEmptyString slug,
        [FromQuery] ReviewSort sort = ReviewSort.Helpful,
        [FromQuery(Name = "rating")] HashSet<short>? ratings = null,
        [FromQuery] bool? hasPhotos = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > MaxPageSize) pageSize = DefaultPageSize;

        var cache = redis.GetDatabase();
        var isFirstPage = page == 1
            && pageSize == DefaultPageSize
            && (ratings is null || ratings.Count == 0)
            && hasPhotos is null
            && sort == ReviewSort.Helpful;

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

        var built = await BuildPageAsync(product.Id, sort, ratings, hasPhotos, page, pageSize, ct);

        if (isFirstPage)
        {
            // Strip per-viewer fields before caching the shared payload.
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

        // Review.Id is UUIDv7, so OrderBy(Id) is time-ordered (no CreatedAtUtc tiebreaker needed).
        var ordered = sort switch
        {
            ReviewSort.Helpful => q.OrderByDescending(r => r.Score).ThenByDescending(r => r.Id),
            ReviewSort.Highest => q.OrderByDescending(r => r.Rating).ThenByDescending(r => r.Id),
            ReviewSort.Lowest  => q.OrderBy(r => r.Rating).ThenByDescending(r => r.Id),
            _ /* Newest */     => q.OrderByDescending(r => r.Id),
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
            ImageUrls = r.ImageUrls,
            Score = r.Score,
            CreatedAtUtc = r.CreatedAtUtc,
            UpdatedAtUtc = r.UpdatedAtUtc,
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

}
