using Temporalio.Workflows;

namespace Reviews.Shared;

public record VoteInput(Guid ReviewId, Guid VoterId, bool IsUpvote);

// Vote workflow per docs/flows.md §4. Wraps the vote UPSERT, the score
// recompute, and the conditional cache refresh in one durable execution so a
// crash mid-write retries the failed step instead of leaving denorm drift.
[Workflow]
public class RateReviewWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(VoteInput input)
    {
        // UpsertVote returns the product slug (so we know which caches to
        // invalidate) or ReviewFound=false if the review doesn't exist.
        var result = await Workflow.ExecuteActivityAsync<VoteResult>(
            ReviewActivityNames.UpsertVote,
            new object[] { input },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        if (!result.ReviewFound) return "review-not-found";

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.InvalidateProductCaches,
            new object[] { result.ProductSlug },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "voted";
    }
}
