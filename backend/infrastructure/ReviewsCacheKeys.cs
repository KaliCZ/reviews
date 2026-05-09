namespace Reviews.Infrastructure;

// Single source of truth for the Redis cache key shapes shared by the API
// (which writes them on cache misses) and the worker activities (which
// invalidate them after every mutation). Keeping the shapes here means a
// rename/refactor only touches one file.
//
// The slug is the catalog-facing identifier on every URL — caching by slug
// avoids an extra slug→id lookup on the read path. The first-page key needs
// the slug too because the worker invalidates *after* a mutation, when the
// product row is already known by id; the worker resolves the slug via
// ProductSlugLookup so its cache invalidations stay slug-keyed.
public static class ReviewsCacheKeys
{
    public const string ProductList = "products:list";

    public static string ProductDetail(string slug) => $"products:slug:{slug}";

    public static string FirstPage(string slug) => $"reviews:slug:{slug}:page:1";

    // The keys an authoring/voting workflow on a given product invalidates:
    // catalog list (review count + average changed), product detail (same),
    // and the first reviews page (the new/edited row may now sit on it).
    public static IEnumerable<string> AffectedBy(string slug)
    {
        yield return ProductList;
        yield return ProductDetail(slug);
        yield return FirstPage(slug);
    }
}
