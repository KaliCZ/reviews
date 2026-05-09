using Temporalio.Workflows;

namespace Reviews.Shared;

public record DeleteReviewInput(Guid ReviewId, Guid AuthorId);

// Mirror of EditReviewWorkflow's policy: deletes inside the first hour are
// the user's mistake/cooling-off case and go through immediately. Older
// deletes get a moderator look — same retroactive-tampering concern.
[Workflow]
public class DeleteReviewWorkflow
{
    public const string ApproveSignal = "Approve";
    public const string RejectSignal = "Reject";

    public static readonly TimeSpan ModerationCutoff = TimeSpan.FromHours(1);

    private ModerationDecision? decision;

    [WorkflowSignal(ApproveSignal)]
    public Task ApproveAsync(string? reason) { decision = new(true, reason); return Task.CompletedTask; }

    [WorkflowSignal(RejectSignal)]
    public Task RejectAsync(string? reason) { decision = new(false, reason); return Task.CompletedTask; }

    [WorkflowRun]
    public async Task<string> RunAsync(DeleteReviewInput input)
    {
        var lookup = await Workflow.ExecuteActivityAsync<ReviewLookupResult>(
            ReviewActivityNames.LookupReview,
            new object[] { input.ReviewId, input.AuthorId },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        if (lookup.Found is false || lookup.OwnedByAuthor is false)
            return "forbidden";

        var age = Workflow.UtcNow - lookup.CreatedAtUtc;
        if (age >= ModerationCutoff)
        {
            await Workflow.WaitConditionAsync(() => decision is not null);
            if (decision!.Approved is false) return "rejected";
        }

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.SoftDeleteReview,
            new object[] { input.ReviewId },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.InvalidateProductCaches,
            new object[] { lookup.ProductSlug },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "deleted";
    }
}
