using StrongTypes;
using Temporalio.Workflows;
using Reviews.Infrastructure.Entities;

namespace Reviews.Shared;

public record EditReviewInput(
    Guid ReviewId,
    Guid AuthorId,
    Rating Rating,
    NonEmptyString Title,
    NonEmptyString Body,
    IReadOnlyList<NonEmptyString> ImageUrls);

// Edits within the first hour go straight through; older edits wait for a
// moderator (retroactive rewrites of already-influential reviews need a look).
[Workflow]
public class EditReviewWorkflow
{
    public const string ApproveSignal = "Approve";
    public const string RejectSignal = "Reject";

    public static readonly TimeSpan ModerationCutoff = TimeSpan.FromHours(1);

    private ModerationDecision? decision;

    [WorkflowSignal(ApproveSignal)]
    public Task ApproveAsync(string? reason = null) { decision = new ModerationDecision(true, reason); return Task.CompletedTask; }

    [WorkflowSignal(RejectSignal)]
    public Task RejectAsync(string? reason = null) { decision = new ModerationDecision(false, reason); return Task.CompletedTask; }

    [WorkflowRun]
    public async Task<string> RunAsync(EditReviewInput input)
    {
        var lookup = await Workflow.ExecuteActivityAsync<ReviewLookupResult>(
            ReviewActivityNames.LookupReview,
            new object[] { input.ReviewId, input.AuthorId },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        if (lookup.Found is false || lookup.OwnedByAuthor is false)
            return "forbidden";

        var age = Workflow.UtcNow - lookup.CreatedAtUtc;
        if (age >= ModerationCutoff)
        {
            await Workflow.WaitConditionAsync(() => decision is not null);
            if (decision!.Approved is false) return "rejected";
        }

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.ApplyReviewEdit,
            new object[] { input },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.RecomputeProductRating,
            new object[] { lookup.ProductId },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.InvalidateProductCaches,
            new object[] { lookup.ProductSlug },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "applied";
    }
}
