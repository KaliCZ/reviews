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
    IReadOnlyList<NonEmptyString> ImageUrls,
    NonEmptyString Language);

// Submit-review flow per docs/flows.md §3:
//   - Every review persists immediately as Pending (the entity ctor's default).
//   - 3- and 4-star reviews flip to Approved right after the persist step,
//     synchronously inside the same workflow run.
//   - 1-, 2-, and 5-star reviews wait for an Approve/Reject signal from a
//     moderator (no timeout — moderation may take days). On Approve they flip
//     to Approved; on Reject they flip to Rejected (the row stays in the DB
//     as the audit trail).
//
// The workflow is the durability boundary: persist + status flip + cache
// invalidation run as separate retried activities, so a crash mid-write can't
// leave the cache and DB out of sync.
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
        // Persist as Pending — the only path that creates Review rows. Done
        // first so the moderator UI (which reads from the DB) can surface the
        // pending row while the workflow waits for a signal. PersistReview
        // returns the product slug so we can invalidate the right cache keys
        // without a follow-up lookup.
        var slug = await Workflow.ExecuteActivityAsync<string>(
            ReviewActivityNames.PersistReview,
            new object[] { input },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

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
                    new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
                // No cache refresh on reject — Rejected rows aren't visible
                // anyway (the listing index is partial on Status = Approved).
                return "rejected";
            }
        }

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.ApproveReview,
            new object[] { input.ReviewId },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        await Workflow.ExecuteActivityAsync(
            ReviewActivityNames.InvalidateProductCaches,
            new object[] { slug },
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(10) });

        return "approved";
    }
}
