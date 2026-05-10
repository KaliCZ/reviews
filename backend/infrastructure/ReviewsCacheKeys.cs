namespace Reviews.Infrastructure;

// Cache key shapes shared by the API (writer) and worker activities (invalidator).
public static class ReviewsCacheKeys
{
    public const string ProductList = "products:list";

    public static string ProductDetail(string slug) => $"products:slug:{slug}";

    // v2: `ReviewItem` gained a `status` field. Old v1 entries deserialize
    // with Status=Pending (enum default) and would mislabel approved rows.
    // Bumping the suffix orphans them and they expire via the 24h TTL.
    public static string FirstPage(string slug) => $"reviews:slug:{slug}:page:1:v2";

    public static IEnumerable<string> AffectedBy(string slug)
    {
        yield return ProductList;
        yield return ProductDetail(slug);
        yield return FirstPage(slug);
    }
}
