using Reviews.Worker;

namespace Reviews.Worker.Tests;

// Smoke test so the runner discovers >0 tests; otherwise xunit.v3 /
// Microsoft.Testing.Platform exits non-zero in CI before any real work
// runs. Asserting that the activities type is wired to the worker assembly
// also catches the common "tests project lost its ProjectReference" gotcha.
public class SmokeTests
{
    [Fact]
    public void ReviewActivities_TypeIsResolvable()
    {
        // Compile-time reference is enough; the assertion just keeps xunit
        // from optimising the import away.
        var t = typeof(ReviewActivities);
        Assert.Equal("ReviewActivities", t.Name);
    }
}
