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

// Mutating endpoints. All require auth, all are rate-limited (per-user AND
// per-IP — both must allow), and submit also requires a verified Cloudflare
// Turnstile token. Each one starts a Temporal workflow and returns the
// workflowId so the SPA can poll status (or pop the review into the Temporal
// UI for moderation if the rating range demands it).
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
        if ((req.ImageUrls?.Count ?? 0) > ReviewsDbContext.MaxImagesPerReview)
            return BadRequest($"At most {ReviewsDbContext.MaxImagesPerReview} images per review.");

        if (!await turnstile.VerifyAsync(req.TurnstileToken.Value, HttpContext.Connection.RemoteIpAddress?.ToString(), ct))
            return BadRequest("Turnstile verification failed.");

        var user = currentUser.User!;

        // Pre-check the unique-author-per-product constraint at the API layer
        // for a fast 409 instead of waiting for the workflow to fail at the
        // INSERT. The DB-level partial unique index is still the real guard.
        var alreadyHas = await db.Reviews
            .AsNoTracking()
            .AnyAsync(r => r.ProductId == req.ProductId
                        && r.AuthorId == user.Id
                        && r.Status != ReviewStatus.Deleted, ct);
        if (alreadyHas) return Conflict("You've already reviewed this product. Edit your existing review instead.");

        var productExists = await db.Products.AsNoTracking().AnyAsync(p => p.Id == req.ProductId, ct);
        if (!productExists) return NotFound($"Product {req.ProductId} not found.");

        // UUIDv7 (sequential / time-ordered) so review_pk inserts append at
        // the right of the btree index instead of scattering across pages.
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
            new(id: $"submit-review-{reviewId:N}", taskQueue: ReviewQueues.TaskQueue));

        return Accepted(new AcceptedResponse
        {
            WorkflowId = handle.Id.ToNonEmpty(),
            Status = "submitted".ToNonEmpty(),
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<AcceptedResponse>> Edit(
        Guid id, [FromBody] EditReviewRequest req, CancellationToken ct)
    {
        if ((req.ImageUrls?.Count ?? 0) > ReviewsDbContext.MaxImagesPerReview)
            return BadRequest($"At most {ReviewsDbContext.MaxImagesPerReview} images per review.");

        var input = new EditReviewInput(
            ReviewId:  id,
            AuthorId:  currentUser.User!.Id,
            Rating:    req.Rating,
            Title:     req.Title,
            Body:      req.Body,
            ImageUrls: req.ImageUrls ?? []);

        var handle = await temporal.StartWorkflowAsync(
            (EditReviewWorkflow wf) => wf.RunAsync(input),
            new(id: $"edit-review-{id:N}-{Sequential.NewGuid():N}", taskQueue: ReviewQueues.TaskQueue));

        return Accepted(new AcceptedResponse
        {
            WorkflowId = handle.Id.ToNonEmpty(),
            Status = "edit-submitted".ToNonEmpty(),
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<AcceptedResponse>> Delete(Guid id, CancellationToken ct)
    {
        var input = new DeleteReviewInput(id, currentUser.User!.Id);
        var handle = await temporal.StartWorkflowAsync(
            (DeleteReviewWorkflow wf) => wf.RunAsync(input),
            new(id: $"delete-review-{id:N}-{Sequential.NewGuid():N}", taskQueue: ReviewQueues.TaskQueue));
        return Accepted(new AcceptedResponse
        {
            WorkflowId = handle.Id.ToNonEmpty(),
            Status = "delete-submitted".ToNonEmpty(),
        });
    }

    [HttpPost("{id:guid}/vote")]
    public async Task<ActionResult<AcceptedResponse>> Vote(
        Guid id, [FromBody] VoteRequest req, CancellationToken ct)
    {
        var user = currentUser.User!;
        var input = new VoteInput(id, user.Id, req.IsUpvote);
        var handle = await temporal.StartWorkflowAsync(
            (RateReviewWorkflow wf) => wf.RunAsync(input),
            // workflow id includes voter so concurrent votes by different
            // users on the same review don't collide; same-user re-votes
            // start fresh executions which the activity collapses via UPSERT.
            new(id: $"vote-{id:N}-{user.Id:N}-{Sequential.NewGuid():N}",
                taskQueue: ReviewQueues.TaskQueue));
        return Accepted(new AcceptedResponse
        {
            WorkflowId = handle.Id.ToNonEmpty(),
            Status = "voted".ToNonEmpty(),
        });
    }
}
