using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Infrastructure.Seeding;

// Idempotent seed run: products + reviews (with image blobs uploaded to
// Azurite). Wrapped under the same advisory lock as the migration runner —
// it's pointless to make the seed itself "smart" about concurrent callers
// when we already have a serialization primitive in front of it.
public static class Seeder
{
    public const string BlobContainer = "review-images";

    private const long LockKey = 738_293_741_829_011L; // distinct from MigrationRunner key

    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ReviewsDbContext>();
        var blobs = sp.GetRequiredService<BlobServiceClient>();
        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient("seed-images");
        var log = sp.GetRequiredService<ILogger<ReviewsDbContext>>();

        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@key)";
                var p = lockCmd.CreateParameter();
                p.ParameterName = "key"; p.Value = LockKey;
                lockCmd.Parameters.Add(p);
                await lockCmd.ExecuteNonQueryAsync(ct);
            }

            // Always ensure the blob container exists and that every seeded
            // image is present in Azurite. Both ops are idempotent (the
            // container create is no-op if it exists; UploadIfMissingAsync
            // skips uploads for blobs that already exist), so it's safe to
            // run on every boot — and decoupled from the product/review
            // insert below, which means a half-seeded state from a previous
            // failed run gets repaired on the next start.
            var container = blobs.GetBlobContainerClient(BlobContainer);
            await container.CreateIfNotExistsAsync(cancellationToken: ct);

            var seedReviews = SeedDefinitions.Reviews().ToList();
            var allSeeds = SeedDefinitions.Products()
                .Select(p => SlugFromUrl(p.ImageUrl?.Value))
                .Concat(seedReviews.SelectMany(r => r.ImageSeeds))
                .Where(s => s is not null)
                .Distinct()
                .ToList();

            foreach (var seed in allSeeds)
                await UploadIfMissingAsync(container, http, seed!, ct);

            // Products + reviews go in once. The unique constraints would
            // catch a re-insert anyway, but the early-return saves the
            // round-trips and keeps the log clean.
            if (await db.Products.AnyAsync(ct))
            {
                log.LogInformation("Products already inserted; ensured {Count} images present", allSeeds.Count);
                return;
            }

            log.LogInformation("Inserting products + reviews");
            await db.Products.AddRangeAsync(SeedDefinitions.Products(), ct);
            foreach (var sr in seedReviews)
            {
                // Seeded rows represent already-moderated demo data — go in
                // pre-Approved with backdated timestamps. The Review.CreateSeed
                // factory is the only path that bypasses the public ctor's
                // "starts as Pending" invariant; restricted to this assembly.
                var imageUrls = sr.ImageSeeds.Select(BuildPublicUrl).ToList();
                var review = Review.CreateSeed(
                    productId:  sr.ProductId,
                    authorId:   sr.AuthorId,
                    authorName: sr.AuthorName.ToNonEmpty(),
                    rating:     sr.Rating,
                    title:      sr.Title.ToNonEmpty(),
                    body:       sr.Body.ToNonEmpty(),
                    imageUrls:  imageUrls,
                    score:      sr.Score,
                    status:     ReviewStatus.Approved,
                    createdAt:  sr.CreatedAt);
                db.Reviews.Add(review);
            }
            await db.SaveChangesAsync(ct);
            log.LogInformation("Seeded {Products} products and {Reviews} reviews",
                SeedDefinitions.Products().Count(), seedReviews.Count);
        }
        finally
        {
            await using var unlock = conn.CreateCommand();
            unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
            var p = unlock.CreateParameter();
            p.ParameterName = "key"; p.Value = LockKey;
            unlock.Parameters.Add(p);
            try { await unlock.ExecuteNonQueryAsync(ct); } catch { /* best effort */ }
            await conn.CloseAsync();
        }
    }

    private static async Task UploadIfMissingAsync(
        BlobContainerClient container, HttpClient http, string seed, CancellationToken ct)
    {
        var blob = container.GetBlobClient(BlobName(seed));
        if (await blob.ExistsAsync(ct)) return;

        // picsum.photos returns a stable image per `seed` so re-runs across
        // dev machines look identical. 600x400 is fine for review thumbnails.
        using var response = await http.GetAsync($"https://picsum.photos/seed/{seed}/800/500", ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await blob.UploadAsync(stream, overwrite: true, cancellationToken: ct);
    }

    private static string BlobName(string seed) => $"seed/{seed}.jpg";
    private static string BuildPublicUrl(string seed) => $"/api/images/{BlobName(seed)}";

    // The seed-defined product image URLs use the public-URL form (`/api/images/seed/...`).
    // Reverse to get the picsum seed for upload-time work.
    private static string? SlugFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        const string prefix = "/api/images/seed/";
        if (!url.StartsWith(prefix)) return null;
        var rest = url[prefix.Length..];
        var dot = rest.LastIndexOf('.');
        return dot < 0 ? rest : rest[..dot];
    }
}
