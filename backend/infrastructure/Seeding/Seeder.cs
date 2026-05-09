using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Infrastructure.Seeding;

// Idempotent seed run: products + reviews (with image blobs uploaded to
// Azurite). Wrapped under a Postgres advisory lock so concurrent callers
// (e.g. multiple API replicas booting at once) serialize on the seed step.
public static class Seeder
{
    public const string BlobContainer = "review-images";

    private const long LockKey = 738_293_741_829_011L;

    public static async Task RunAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<ReviewsDbContext>();
        var blobs = sp.GetRequiredService<BlobServiceClient>();
        var http = sp.GetRequiredService<SeedImageDownloader>();
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
                    id:         Sequential.NewGuid(),
                    productId:  sr.ProductId,
                    authorId:   sr.AuthorId,
                    authorName: sr.AuthorName.ToNonEmpty(),
                    rating:     sr.Rating,
                    title:      sr.Title.ToNonEmpty(),
                    body:       sr.Body.ToNonEmpty(),
                    imageUrls:  imageUrls,
                    language:   sr.Language.ToNonEmpty(),
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
            try
            {
                await unlock.ExecuteNonQueryAsync(ct);
            }
            catch (Exception ex)
            {
                // Closing the connection drops the session-level advisory
                // lock anyway, so this isn't fatal — but a failure here is a
                // signal that something odd happened (network blip, rollback
                // mid-finally), and the alternative of swallowing silently
                // makes investigation in production harder than it needs
                // to be.
                log.LogError(ex, "Failed to release seed advisory lock {LockKey}; lock will be released on connection close", LockKey);
            }
            await conn.CloseAsync();
        }
    }

    private static async Task UploadIfMissingAsync(
        BlobContainerClient container, SeedImageDownloader http, string seed, CancellationToken ct)
    {
        var blob = container.GetBlobClient(BlobName(seed));
        if (await blob.ExistsAsync(ct)) return;

        // picsum.photos returns a stable image per `seed` so re-runs across
        // dev machines look identical. 800x500 is fine for review thumbnails.
        using var response = await http.GetAsync(seed, ct);
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
