using Azure;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Mvc;
using Reviews.Infrastructure.Seeding;

namespace Reviews.Api.Controllers;

// Streams blobs from Azurite/Azure Blob through the API rather than handing
// out direct blob URLs. Two reasons:
//   1. The browser-visible URL (`/api/images/...`) is environment-agnostic —
//      same in dev, compose, and prod, regardless of how blob storage is
//      reached internally.
//   2. We can later add access control (private blobs, auth, watermarking)
//      without churning every URL stored in the DB.
//
// Trade-off: API is in the hot path for image bytes. For a production catalog
// you'd swap to direct CDN URLs and store those instead. Acceptable for the
// kickoff — the indirection is cheap and the simplicity is worth more than
// the bandwidth.
[ApiController]
[Route("api/[controller]")]
public class ImagesController(BlobServiceClient blobs) : ControllerBase
{
    [HttpGet("{**path}")]
    public async Task<IActionResult> Get(string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NotFound();

        var container = blobs.GetBlobContainerClient(Seeder.BlobContainer);
        var blob = container.GetBlobClient(path);

        try
        {
            var props = await blob.GetPropertiesAsync(cancellationToken: ct);
            var stream = await blob.OpenReadAsync(cancellationToken: ct);
            // Cache hard — the URL is content-keyed by review id / seed name,
            // and our flows replace rather than mutate (review edits with new
            // images get new keys).
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return File(stream, props.Value.ContentType ?? "image/jpeg");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return NotFound();
        }
    }
}
