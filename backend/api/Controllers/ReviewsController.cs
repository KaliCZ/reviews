using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reviews.Api.Auth;
using Reviews.Api.Models;
using Reviews.Api.Services;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Caching;
using Reviews.Infrastructure.Entities;
using Reviews.Shared;
using StrongTypes;
using Temporalio.Client;

namespace Reviews.Api.Controllers;

[ApiController]
[Authorize]
[EnableRateLimiting(AuthExtensions.WriteRateLimitPolicy)]
[Route("api/[controller]")]
public class ReviewsController(
    ReviewsDbContext db,
    ITemporalClient temporal,
    ICurrentUserAccessor currentUser,
    ITurnstileVerifier turnstile,
    IReviewCacheInvalidator cacheInvalidator) : ControllerBase
{
    // Header carries the Turnstile token for endpoints without a JSON body
    // (DELETE). Submit/edit/vote pass it in the body instead.
    public const string TurnstileHeader = "X-Turnstile-Token";

    // Author-scoped read so the edit page can load Pending reviews — the
    // public listing under /api/products/{slug}/reviews only surfaces Approved,
    // so without this the user can't edit a review until it's approved.
    [HttpGet("{id:guid}")]
    [DisableRateLimiting]
    public async Task<ActionResult<ReviewItem>> Get(Guid id, CancellationToken ct)
    {
        var user = currentUser.User!;

        var item = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == id && r.AuthorId == user.Id && r.Status != ReviewStatus.Deleted)
            .Select(r => new ReviewItem
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
                Mine = true,
            })
            .SingleOrDefaultAsync(ct);

        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<ActionResult<AcceptedResponse>> Submit(
        [FromBody] SubmitReviewRequest req, CancellationToken ct)
    {
        if (ValidateImageUrls(req.ImageUrls) is { } error) return BadRequest(error);

        if (!await VerifyTurnstileAsync(req.TurnstileToken.Value, ct))
            return BadRequest("Turnstile verification failed.");

        var user = currentUser.User!;

        // Fast 409 instead of waiting for the workflow to fail at INSERT;
        // the partial unique index is still the real guard.
        var alreadyHas = await db.Reviews
            .AsNoTracking()
            .AnyAsync(r => r.ProductId == req.ProductId
                        && r.AuthorId == user.Id
                        && r.Status != ReviewStatus.Deleted, ct);
        if (alreadyHas) return Conflict("You've already reviewed this product. Edit your existing review instead.");

        var productExists = await db.Products.AsNoTracking().AnyAsync(p => p.Id == req.ProductId, ct);
        if (!productExists) return NotFound($"Product {req.ProductId} not found.");

        var reviewId = Sequential.NewGuid();
        var input = new SubmitReviewInput(
            ReviewId:   reviewId,
            ProductId:  req.ProductId,
            AuthorId:   user.Id,
            AuthorName: user.Name,
            Rating:     req.Rating,
            Title:      req.Title,
            Body:       req.Body,
            ImageUrls:  req.ImageUrls ?? []);

        var handle = await temporal.StartWorkflowAsync(
            (SubmitReviewWorkflow wf) => wf.RunAsync(input),
            new WorkflowOptions(id: $"submit-review-{reviewId:N}", taskQueue: ReviewQueues.TaskQueue));

        return Accepted(new AcceptedResponse(handle.Id, "submitted"));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AcceptedResponse>> Edit(
        Guid id, [FromBody] EditReviewRequest req, CancellationToken ct)
    {
        if (ValidateImageUrls(req.ImageUrls) is { } error) return BadRequest(error);

        if (!await VerifyTurnstileAsync(req.TurnstileToken.Value, ct))
            return BadRequest("Turnstile verification failed.");

        var input = new EditReviewInput(
            ReviewId:  id,
            AuthorId:  currentUser.User!.Id,
            Rating:    req.Rating,
            Title:     req.Title,
            Body:      req.Body,
            ImageUrls: req.ImageUrls ?? []);

        var handle = await temporal.StartWorkflowAsync(
            (EditReviewWorkflow wf) => wf.RunAsync(input),
            new WorkflowOptions(id: $"edit-review-{id:N}-{Sequential.NewGuid():N}", taskQueue: ReviewQueues.TaskQueue));

        return Accepted(new AcceptedResponse(handle.Id, "edit-submitted"));
    }

    // Synchronous: the user owns the review and chose to delete it. Recent-auth
    // (auth_time) is enforced to make this destructive action expensive for
    // anyone replaying a stolen access token.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (RecentAuth.RequireFresh(User, RecentAuth.DeleteFreshness, Response) is { } challenge)
            return challenge;

        var token = Request.Headers[TurnstileHeader].ToString();
        if (string.IsNullOrEmpty(token) || !await VerifyTurnstileAsync(token, ct))
            return BadRequest("Turnstile verification failed.");

        var user = currentUser.User!;

        var lookup = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == id && r.Status != ReviewStatus.Deleted)
            .Select(r => new { r.AuthorId, r.ProductId, ProductSlug = r.Product.Slug.Value })
            .SingleOrDefaultAsync(ct);
        if (lookup is null) return NotFound();
        if (lookup.AuthorId != user.Id) return Forbid();

        // ExecuteAsync wraps the soft-delete + rating recompute in one
        // strategy-managed unit so the retrying execution strategy
        // (Aspire-wired NpgsqlRetryingExecutionStrategy) can replay it.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            await db.Reviews
                .Where(r => r.Id == id && r.Status != ReviewStatus.Deleted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Status, ReviewStatus.Deleted)
                    .SetProperty(r => r.UpdatedAtUtc, _ => DateTime.UtcNow), ct);

            await db.Products
                .Where(p => p.Id == lookup.ProductId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.ReviewCount, p => p.Reviews
                        .Count(r => r.Status == ReviewStatus.Approved))
                    .SetProperty(p => p.AverageRating, p => p.Reviews
                        .Where(r => r.Status == ReviewStatus.Approved)
                        .Average(r => (double?)(short)r.Rating) ?? 0), ct);

            await tx.CommitAsync(ct);
        });

        // Best-effort: invalidator retries internally; the 24h TTL backstops
        // a permanent miss and the next mutation re-DEL's the same keys.
        await cacheInvalidator.InvalidateProductAsync(lookup.ProductSlug, ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/vote")]
    public async Task<ActionResult<VoteResponse>> Vote(
        Guid id, [FromBody] VoteRequest req, CancellationToken ct)
    {
        if (!await VerifyTurnstileAsync(req.TurnstileToken.Value, ct))
            return BadRequest("Turnstile verification failed.");

        var user = currentUser.User!;

        // Pinning to Approved blocks votes on deleted/rejected/pending reviews
        // even from a stale client.
        var slug = await db.Reviews
            .AsNoTracking()
            .Where(r => r.Id == id && r.Status == ReviewStatus.Approved)
            .Select(r => (string?)r.Product.Slug.Value)
            .SingleOrDefaultAsync(ct);
        if (slug is null) return NotFound();

        // Aspire wires NpgsqlRetryingExecutionStrategy into the DbContext;
        // user-initiated transactions must be wrapped in ExecuteAsync so the
        // upsert + score recompute live in one strategy-managed unit.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var updated = await db.ReviewVotes
                .Where(v => v.ReviewId == id && v.VoterId == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsUpvote, req.IsUpvote), ct);
            if (updated == 0)
            {
                db.ReviewVotes.Add(new ReviewVote(id, user.Id, req.IsUpvote));
                await db.SaveChangesAsync(ct);
            }

            // Recompute Score from vote rows so it self-heals on every write.
            await db.Reviews
                .Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.Score, _ => db.ReviewVotes
                        .Where(v => v.ReviewId == id)
                        .Sum(v => v.IsUpvote ? 1 : -1))
                    .SetProperty(r => r.UpdatedAtUtc, _ => DateTime.UtcNow), ct);

            await tx.CommitAsync(ct);
        });

        await cacheInvalidator.InvalidateProductAsync(slug, ct);

        // Read after commit + invalidation so we surface the latest committed
        // score (picking up any concurrent voter that raced us) and don't hold
        // tx locks for the round-trip.
        return Ok(new VoteResponse(
            await db.Reviews.AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => r.Score)
                .SingleAsync(ct),
            req.IsUpvote));
    }

    private Task<bool> VerifyTurnstileAsync(string token, CancellationToken ct) =>
        turnstile.VerifyAsync(token, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

    // Mirrors the DB CHECK constraints so 400 fires before the workflow starts.
    private static string? ValidateImageUrls(IReadOnlyList<NonEmptyString>? urls)
    {
        if (urls is null) return null;
        if (urls.Count > ReviewsDbContext.MaxImagesPerReview)
            return $"At most {ReviewsDbContext.MaxImagesPerReview} images per review.";
        foreach (var u in urls)
            if (u.Value.Length > ReviewsDbContext.ReviewImageUrlMaxLength)
                return $"Image URL exceeds {ReviewsDbContext.ReviewImageUrlMaxLength} characters.";
        return null;
    }
}
