using Reviews.Infrastructure;

namespace Reviews.Api.Tests;

// ReviewsCacheKeys is the single source of truth for the Redis key shapes
// shared by the API (writers on cache miss) and the worker (invalidators
// after every mutation). Pin the literal strings so a rename or template
// tweak surfaces in a test diff — both sides have to agree on the bytes.
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
        // The caller asks for "first page of reviews for slug X"; the key
        // carries `:page:1` literally so a future cursor / per-page caching
        // tier can slot in alongside.
        Assert.Equal("reviews:slug:foo:page:1", ReviewsCacheKeys.FirstPage("foo"));
    }

    [Fact]
    public void AffectedBy_returns_three_distinct_keys()
    {
        // The worker invalidates exactly: catalog list, product detail,
        // first reviews page. Three keys, no duplicates.
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
        // Documents the assumption that callers pass a URL-safe slug. The
        // upstream validator (NonEmptyString + DB unique index on a slug
        // column with a sane regex) is the gate; the key builder is a
        // dumb formatter.
        Assert.Equal("products:slug:has:colon", ReviewsCacheKeys.ProductDetail("has:colon"));
    }
}
