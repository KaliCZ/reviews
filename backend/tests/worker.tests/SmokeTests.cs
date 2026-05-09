using Reviews.Worker;

namespace Reviews.Worker.Tests;

// Microsoft.Testing.Platform fails CI on zero-test discovery; this keeps the
// runner happy and fails loudly if the project loses its ProjectReference.
public class SmokeTests
{
    [Fact]
    public void ReviewActivities_TypeIsResolvable()
    {
        var t = typeof(ReviewActivities);
        Assert.Equal("ReviewActivities", t.Name);
    }
}
