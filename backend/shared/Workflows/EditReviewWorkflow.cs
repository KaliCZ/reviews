using Temporalio.Workflows;

namespace Reviews.Shared;

public record EditReviewInput(
    Guid ReviewId,
    Guid AuthorId,
    short Rating,
    string? Title,
    string Body,
    IReadOnlyList<string> ImageUrls);

// Edits to recent reviews go straight through; edits to reviews older than an
// hour wait for a moderator signal first. The cutoff exists because once a
// review has had time to influence other readers' decisions, retroactive
// rewriting is the kind of thing a moderator should see.
[Workflow]
public class EditReviewWorkflow
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
    public async Task<string> RunAsync(EditReviewInput input)
    {
        var lookup = await Workflow.ExecuteActivityAsync<ReviewLookupResult>(
            ReviewActivityNames.LookupReview,
            new object[] { input.ReviewId, input.AuthorId },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        if (lookup.Found is false || lookup.OwnedByAuthor is false)
            return "forbidden";

        var age = Workflow.UtcNow - lookup.CreatedAt;
        if (age >= ModerationCutoff)
        {
            await Workflow.WaitConditionAsync(() => decision is not null);
            if (decision!.Approved is false) return "rejected";
        }

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.ApplyReviewEdit,
            new object[] { input },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.RefreshFirstPageCache,
            new object[] { lookup.ProductId },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "applied";
    }
}
