using Reviews.Infrastructure;

namespace Reviews.Api.Tests;

// API and worker have to agree on the cache key bytes; pin the literals so a
// rename surfaces in a test diff.
public class ReviewsCacheKeysTests
{
    [Fact]
    public void ProductList_is_literal_constant()
    {
        Assert.Equal("products:list", ReviewsCacheKeys.ProductList);
    }

    [Fact]
    public void ProductDetail_uses_slug_namespace()
    {
        Assert.Equal("products:slug:foo", ReviewsCacheKeys.ProductDetail("foo"));
    }

    [Fact]
    public void FirstPage_includes_page_number()
    {
        Assert.Equal("reviews:slug:foo:page:1", ReviewsCacheKeys.FirstPage("foo"));
    }

    [Fact]
    public void AffectedBy_returns_three_distinct_keys()
    {
        var keys = ReviewsCacheKeys.AffectedBy("foo").ToList();
        Assert.Equal(3, keys.Count);
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.Contains("products:list", keys);
        Assert.Contains("products:slug:foo", keys);
        Assert.Contains("reviews:slug:foo:page:1", keys);
    }

    [Fact]
    public void Slug_with_colon_is_preserved_verbatim()
    {
        // The key builder is a dumb formatter; slug sanitization is upstream.
        Assert.Equal("products:slug:has:colon", ReviewsCacheKeys.ProductDetail("has:colon"));
    }
}
