namespace Reviews.Api.Tests;

// Pins constants the SPA's Limits.maxImageBytes and the docs depend on.
public class DtoContractTests
{
    [Fact]
    public void Image_upload_constants_match_documented_limits()
    {
        Assert.Equal(2L * 1024 * 1024, Reviews.Api.Controllers.ImagesController.MaxImageBytes);
    }
}
