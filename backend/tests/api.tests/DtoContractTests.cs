namespace Reviews.Api.Tests;

// Pins documented limit constants that are referenced from multiple sides
// (the SPA's `Limits.maxImageBytes`, the docs, and the controller). Wire-shape
// and DTO round-trip tests were dropped because the integration suite at
// Integration/ApiIntegrationTests.cs exercises those over real HTTP/JSON and
// catches breaking changes that typed-DTO unit tests would silently pass.
public class DtoContractTests
{
    [Fact]
    public void Image_upload_constants_match_documented_limits()
    {
        // The 2 MiB cap referenced in the SPA's `Limits.maxImageBytes` and
        // the docs is wired to ImagesController.MaxImageBytes — pin the
        // value so a future tweak shows up in test diffs across both sides.
        Assert.Equal(2L * 1024 * 1024, Reviews.Api.Controllers.ImagesController.MaxImageBytes);
    }
}
