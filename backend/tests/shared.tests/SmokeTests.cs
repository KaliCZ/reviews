using Reviews.Shared;

namespace Reviews.Shared.Tests;

// Microsoft.Testing.Platform fails CI on zero-test discovery.
public class SmokeTests
{
    [Fact]
    public void TaskQueueName_IsStable()
    {
        // API and worker pin this string at compile time; renaming breaks routing.
        Assert.Equal("reviews", ReviewQueues.TaskQueue);
    }

    [Fact]
    public void ActivityNames_AreUnique()
    {
        // Temporal routes by activity name; duplicates silently invoke the last one registered.
        var names = new[]
        {
            ReviewActivityNames.PersistReview,
            ReviewActivityNames.ApproveReview,
            ReviewActivityNames.RejectReview,
            ReviewActivityNames.LookupReview,
            ReviewActivityNames.ApplyReviewEdit,
            ReviewActivityNames.SoftDeleteReview,
            ReviewActivityNames.RecordVote,
            ReviewActivityNames.InvalidateProductCaches,
        };
        Assert.Equal(names.Length, names.Distinct().Count());
    }
}
