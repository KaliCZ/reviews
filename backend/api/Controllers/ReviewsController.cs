using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Reviews.Api.Auth;
using Reviews.Api.Models;
using Reviews.Api.Services;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Entities;
using Reviews.Shared;
using StrongTypes;
using Temporalio.Client;

namespace Reviews.Api.Controllers;

// Mutating endpoints. All require auth, all are rate-limited, and submit also
// requires a verified Cloudflare Turnstile token. Each one starts a Temporal
// workflow and returns the workflowId so the SPA can poll status (or pop the
// review into the Temporal UI for moderation if the rating range demands it).
[ApiController]
[Authorize]
[EnableRateLimiting(AuthExtensions.WriteRateLimitPolicy)]
[Route("api/[controller]")]
public class ReviewsController(
    ReviewsDbContext db,
    ITemporalClient temporal,
    ICurrentUser currentUser,
    ITurnstileVerifier turnstile) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<AcceptedResponse>> Submit(
        [FromBody] SubmitReviewRequest req, CancellationToken ct)
    {
        // Rating is the only field the wrappers don't validate for us — it's
        // a primitive `short` (1..5) and StrongTypes has no Range<T>. Title /
        // Body / TurnstileToken empties already failed at deserialization.
        if (req.Rating is < 1 or > 5) return BadRequest("Rating must be between 1 and 5.");
        if ((req.ImageUrls?.Count ?? 0) > ReviewsDbContext.MaxImagesPerReview)
            return BadRequest($"At most {ReviewsDbContext.MaxImagesPerReview} images per review.");

        if (!await turnstile.VerifyAsync(req.TurnstileToken.Value, HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
            return BadRequest("Turnstile verification failed.");

        var uid = currentUser.Id!.Value;

        // Pre-check the unique-author-per-product constraint at the API layer
        // for a fast 409 instead of waiting for the workflow to fail at the
        // INSERT. The DB-level partial unique index is still the real guard.
        var alreadyHas = await db.Reviews
            .AsNoTracking()
            .AnyAsync(r => r.ProductId == req.ProductId
                        && r.AuthorId == uid
                        && r.Status != ReviewStatus.Deleted, ct);
        if (alreadyHas) return Conflict("You've already reviewed this product. Edit your existing review instead.");

        var productExists = await db.Products.AsNoTracking().AnyAsync(p => p.Id == req.ProductId, ct);
        if (!productExists) return NotFound($"Product {req.ProductId} not found.");

        var reviewId = Guid.NewGuid();
        var input = new SubmitReviewInput(
            ReviewId:   reviewId,
            ProductId:  req.ProductId,
            AuthorId:   uid,
            AuthorName: (currentUser.Name ?? "Anonymous").ToNonEmpty(),
            Rating:     req.Rating,
            Title:      req.Title,
            Body:       req.Body,
            ImageUrls:  req.ImageUrls ?? []);

        var handle = await temporal.StartWorkflowAsync(
            (SubmitReviewWorkflow wf) => wf.RunAsync(input),
            new(id: $"submit-review-{reviewId:N}", taskQueue: ReviewQueues.TaskQueue));

        return Accepted(new AcceptedResponse(handle.Id.ToNonEmpty(), "submitted".ToNonEmpty()));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AcceptedResponse>> Edit(
        Guid id, [FromBody] EditReviewRequest req, CancellationToken ct)
    {
        if (req.Rating is < 1 or > 5) return BadRequest("Rating must be between 1 and 5.");
        if ((req.ImageUrls?.Count ?? 0) > ReviewsDbContext.MaxImagesPerReview)
            return BadRequest($"At most {ReviewsDbContext.MaxImagesPerReview} images per review.");

        var input = new EditReviewInput(
            ReviewId:  id,
            AuthorId:  currentUser.Id!.Value,
            Rating:    req.Rating,
            Title:     req.Title,
            Body:      req.Body,
            ImageUrls: req.ImageUrls ?? []);

        var handle = await temporal.StartWorkflowAsync(
            (EditReviewWorkflow wf) => wf.RunAsync(input),
            new(id: $"edit-review-{id:N}-{Guid.NewGuid():N}", taskQueue: ReviewQueues.TaskQueue));

        return Accepted(new AcceptedResponse(handle.Id.ToNonEmpty(), "edit-submitted".ToNonEmpty()));
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<AcceptedResponse>> Delete(Guid id, CancellationToken ct)
    {
        var input = new DeleteReviewInput(id, currentUser.Id!.Value);
        var handle = await temporal.StartWorkflowAsync(
            (DeleteReviewWorkflow wf) => wf.RunAsync(input),
            new(id: $"delete-review-{id:N}-{Guid.NewGuid():N}", taskQueue: ReviewQueues.TaskQueue));
        return Accepted(new AcceptedResponse(handle.Id.ToNonEmpty(), "delete-submitted".ToNonEmpty()));
    }

    [HttpPost("{id:guid}/vote")]
    public async Task<ActionResult<AcceptedResponse>> Vote(
        Guid id, [FromBody] VoteRequest req, CancellationToken ct)
    {
        if (req.Value is not (1 or -1)) return BadRequest("Vote value must be +1 or -1.");

        var input = new VoteInput(id, currentUser.Id!.Value, req.Value);
        var handle = await temporal.StartWorkflowAsync(
            (RateReviewWorkflow wf) => wf.RunAsync(input),
            // workflow id includes voter so concurrent votes by different
            // users on the same review don't collide; same-user re-votes
            // start fresh executions which the activity collapses via UPSERT.
            new(id: $"vote-{id:N}-{currentUser.Id:N}-{Guid.NewGuid():N}",
                taskQueue: ReviewQueues.TaskQueue));
        return Accepted(new AcceptedResponse(handle.Id.ToNonEmpty(), "voted".ToNonEmpty()));
    }
}
