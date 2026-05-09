using StrongTypes;
using Temporalio.Workflows;
using Reviews.Infrastructure.Entities;

namespace Reviews.Shared;

public record SubmitReviewInput(
    Guid ReviewId,
    long ProductId,
    Guid AuthorId,
    NonEmptyString AuthorName,
    Rating Rating,
    NonEmptyString Title,
    NonEmptyString Body,
    IReadOnlyList<NonEmptyString> ImageUrls);

// Submit-review flow per docs/flows.md §3: persist as Pending, then 3/4-star
// auto-approve while 1/2/5-star wait on a moderator signal (no timeout).
// Rejected rows stay in the DB as the audit trail.
[Workflow]
public class SubmitReviewWorkflow
{
    public const string ApproveSignal = "Approve";
    public const string RejectSignal = "Reject";

    private ModerationDecision? decision;

    [WorkflowSignal(ApproveSignal)]
    public Task ApproveAsync(string? reason)
    {
        decision = new ModerationDecision(true, reason);
        return Task.CompletedTask;
    }

    [WorkflowSignal(RejectSignal)]
    public Task RejectAsync(string? reason)
    {
        decision = new ModerationDecision(false, reason);
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<string> RunAsync(SubmitReviewInput input)
    {
        // Persist first so the moderator UI (DB-backed) can surface the
        // Pending row while the workflow waits for a signal.
        var slug = await Workflow.ExecuteActivityAsync<string>(
            ReviewActivityNames.PersistReview,
            new object[] { input },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        var rating = input.Rating;
        var needsModeration = rating is Rating.One or Rating.Two or Rating.Five;
        if (needsModeration)
        {
            await Workflow.WaitConditionAsync(() => decision is not null);
            if (decision!.Approved is false)
            {
                await Workflow.ExecuteActivityAsync(
                    ReviewActivityNames.RejectReview,
                    new object[] { input.ReviewId },
                    new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
                // No cache invalidate: listing index is partial on Status=Approved.
                return "rejected";
            }
        }

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.ApproveReview,
            new object[] { input.ReviewId },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.InvalidateProductCaches,
            new object[] { slug },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "approved";
    }
}
