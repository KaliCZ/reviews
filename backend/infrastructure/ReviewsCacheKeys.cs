namespace Reviews.Infrastructure;

// Cache key shapes shared by the API (writer) and worker activities (invalidator).
public static class ReviewsCacheKeys
{
    public const string ProductList = "products:list";

    public static string ProductDetail(string slug) => $"products:slug:{slug}";

    public static string FirstPage(string slug) => $"reviews:slug:{slug}:page:1";

    public static IEnumerable<string> AffectedBy(string slug)
    {
        yield return ProductList;
        yield return ProductDetail(slug);
        yield return FirstPage(slug);
    }
}
