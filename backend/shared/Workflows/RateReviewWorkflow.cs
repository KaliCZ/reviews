using Temporalio.Workflows;

namespace Reviews.Shared;

public record VoteInput(Guid ReviewId, Guid VoterId, bool IsUpvote);

// Vote workflow per docs/flows.md §4. Wraps the vote write, the score
// recompute, and the conditional cache refresh in one durable execution so a
// crash mid-write retries the failed step instead of leaving denorm drift.
//
// The workflow id is deterministic per (review, voter) — see ReviewsController
// — and the start uses WorkflowIdConflictPolicy.UseExisting so concurrent
// votes by the same user serialize on the workflow id rather than racing each
// other on the row.
[Workflow]
public class RateReviewWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(VoteInput input)
    {
        // RecordVote returns the product slug (so we know which caches to
        // invalidate) or ReviewFound=false if the review doesn't exist.
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
