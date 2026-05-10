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
    [HttpPost]
    public async Task<ActionResult<AcceptedResponse>> Submit(
        [FromBody] SubmitReviewRequest req, CancellationToken ct)
    {
        if (ValidateImageUrls(req.ImageUrls) is { } error) return BadRequest(error);

        if (!await turnstile.VerifyAsync(req.TurnstileToken.Value, HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
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

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<AcceptedResponse>> Delete(Guid id, CancellationToken ct)
    {
        var input = new DeleteReviewInput(id, currentUser.User!.Id);
        var handle = await temporal.StartWorkflowAsync(
            (DeleteReviewWorkflow wf) => wf.RunAsync(input),
            new WorkflowOptions(id: $"delete-review-{id:N}-{Sequential.NewGuid():N}", taskQueue: ReviewQueues.TaskQueue));
        return Accepted(new AcceptedResponse(handle.Id, "delete-submitted"));
    }

    [HttpPost("{id:guid}/vote")]
    public async Task<ActionResult<VoteResponse>> Vote(
        Guid id, [FromBody] VoteRequest req, CancellationToken ct)
    {
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
        var newScore = await strategy.ExecuteAsync(async () =>
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

            var score = await db.Reviews.AsNoTracking()
                .Where(r => r.Id == id)
                .Select(r => r.Score)
                .SingleAsync(ct);

            await tx.CommitAsync(ct);
            return score;
        });

        // Best-effort: invalidator retries internally; a permanent miss is
        // backstopped by the 24h TTL and the next mutation re-DEL'ing the
        // same keys.
        await cacheInvalidator.InvalidateProductAsync(slug, ct);

        return Ok(new VoteResponse(newScore, req.IsUpvote));
    }

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
