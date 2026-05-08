using Temporalio.Workflows;

namespace Reviews.Shared;

public record SubmitReviewInput(
    Guid ReviewId,
    long ProductId,
    Guid AuthorId,
    string AuthorName,
    short Rating,
    string? Title,
    string Body,
    IReadOnlyList<string> ImageUrls);

// Submit-review flow per docs/flows.md §3:
//   - 3- and 4-star reviews persist immediately.
//   - 1-, 2-, and 5-star reviews wait for an Approve/Reject signal from a
//     moderator. Today the moderator sends that signal manually from the
//     Temporal UI; later it'll be a moderation tool / MCP surface.
// The workflow is the durability boundary: persist + first-page-cache refresh
// run as separate retried activities, so a crash mid-write can't leave the
// cache and DB out of sync.
[Workflow]
public class SubmitReviewWorkflow
{
    public const string ApproveSignal = "Approve";
    public const string RejectSignal = "Reject";

    private ModerationDecision? decision;

    [WorkflowSignal(ApproveSignal)]
    public Task ApproveAsync(string? reason)
    {
        decision = new(true, reason);
        return Task.CompletedTask;
    }

    [WorkflowSignal(RejectSignal)]
    public Task RejectAsync(string? reason)
    {
        decision = new(false, reason);
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<string> RunAsync(SubmitReviewInput input)
    {
        var needsModeration = input.Rating is 1 or 2 or 5;

        if (needsModeration)
        {
            // No timeout — the docs are explicit that human moderation may
            // take days. Temporal's durable timers + signal-and-resume are the
            // whole reason this lives in a workflow rather than a job queue.
            await Workflow.WaitConditionAsync(() => decision is not null);
            // On reject, we don't write anything — the review never reached the
            // database, and the workflow's own history is the audit trail.
            if (decision!.Approved is false) return "rejected";
        }

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.PersistReview,
            new object[] { input },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.RefreshFirstPageCache,
            new object[] { input.ProductId },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "approved";
    }
}
