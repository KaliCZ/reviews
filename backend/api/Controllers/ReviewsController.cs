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
    public async Task<ActionResult<AcceptedResponse>> Vote(
        Guid id, [FromBody] VoteRequest req, CancellationToken ct)
    {
        var user = currentUser.User!;
        var input = new VoteInput(id, user.Id, req.IsUpvote);
        // Deterministic workflow id per (review, voter) + UseExisting:
        // rapid-fire votes from the same user serialize on the server and
        // double-clicks join the in-flight execution instead of erroring.
        var handle = await temporal.StartWorkflowAsync(
            (RateReviewWorkflow wf) => wf.RunAsync(input),
            new WorkflowOptions(id: $"vote-{id:N}-{user.Id:N}", taskQueue: ReviewQueues.TaskQueue)
            {
                IdConflictPolicy = Temporalio.Api.Enums.V1.WorkflowIdConflictPolicy.UseExisting,
            });
        return Accepted(new AcceptedResponse(handle.Id, "voted"));
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
