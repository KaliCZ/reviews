using Temporalio.Workflows;

namespace Reviews.Shared;

public record VoteInput(Guid ReviewId, Guid VoterId, short Value);

// Vote workflow per docs/flows.md §4. Wraps the vote UPSERT, the score
// recompute, and the conditional cache refresh in one durable execution so a
// crash mid-write retries the failed step instead of leaving denorm drift.
[Workflow]
public class RateReviewWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(VoteInput input)
    {
        // UpsertVote returns the product_id of the review (so we know which
        // page-1 cache to invalidate) or null if the review doesn't exist.
        var productId = await Workflow.ExecuteActivityAsync<long?>(
            ReviewActivityNames.UpsertVote,
            new object[] { input },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        if (productId is null) return "review-not-found";

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.RefreshFirstPageCache,
            new object[] { productId.Value },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "voted";
    }
}
