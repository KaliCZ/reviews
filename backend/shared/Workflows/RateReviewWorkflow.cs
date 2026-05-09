using Temporalio.Workflows;

namespace Reviews.Shared;

public record VoteInput(Guid ReviewId, Guid VoterId, bool IsUpvote);

// Vote workflow per docs/flows.md §4. Wraps vote write + score recompute +
// cache invalidate in one durable execution.
[Workflow]
public class RateReviewWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(VoteInput input)
    {
        var result = await Workflow.ExecuteActivityAsync<VoteResult>(
            ReviewActivityNames.RecordVote,
            new object[] { input },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        if (!result.ReviewFound) return "review-not-found";

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.InvalidateProductCaches,
            new object[] { result.ProductSlug },
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "voted";
    }
}
