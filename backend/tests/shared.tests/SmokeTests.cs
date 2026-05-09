using Reviews.Shared;

namespace Reviews.Shared.Tests;

// Smoke tests so the test runner reports >0 tests. Microsoft.Testing.Platform
// (the runner xunit.v3 ships with) treats a zero-test discovery as a failure
// in CI, which fails the Backend job before any real work runs. The asserts
// here also double as a dependency check: if the shared project fails to
// reference cleanly the project simply won't compile.
public class SmokeTests
{
    [Fact]
    public void TaskQueueName_IsStable()
    {
        // The API and worker both pin to this string at compile time. If
        // someone "tidies" the constant they break workflow routing — assert
        // the literal value here so the rename shows up in test diffs.
        Assert.Equal("reviews", ReviewQueues.TaskQueue);
    }

    [Fact]
    public void ActivityNames_AreUnique()
    {
        // Temporal routes by activity name; two activities sharing a name
        // would silently invoke whichever registered last. Cheap guard.
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
