using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reviews.Api.Models;
using Reviews.Infrastructure.Seeding;
using StrongTypes;

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
public class ImagesController(BlobServiceClient blobs, ILogger<ImagesController> logger) : ControllerBase
{
    // Per-file ceiling for review uploads. Enforced both as a multipart
    // request-body limit (so the framework rejects oversize before we read
    // anything) and as a defensive second check on the IFormFile.Length —
    // request limits can be relaxed by middleware, the Length check can't.
    public const long MaxImageBytes = 2L * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/gif",
    };

    [AllowAnonymous]
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

    // POST /api/images — upload a single review image. Returns the public URL
    // the SPA stores in the review's ImageUrls. Rejects payloads larger than
    // 2 MiB and content types outside the allow-list (the SPA disables the
    // file picker for everything else, but the server is the real gate).
    [Authorize]
    [HttpPost]
    [RequestSizeLimit(MaxImageBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImageBytes)]
    public async Task<ActionResult<UploadedImage>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest("File is required.");

        if (file.Length > MaxImageBytes)
            return BadRequest($"File too large. Max size is {MaxImageBytes / (1024 * 1024)} MB.");

        if (file.ContentType is null || !AllowedContentTypes.Contains(file.ContentType))
            return BadRequest($"Unsupported content type. Allowed: {string.Join(", ", AllowedContentTypes)}.");

        var ext = file.ContentType switch
        {
            "image/png" => "png",
            "image/webp" => "webp",
            "image/gif" => "gif",
            _ => "jpg",
        };
        var key = $"uploads/{Guid.NewGuid():N}.{ext}";

        var container = blobs.GetBlobContainerClient(Seeder.BlobContainer);
        await container.CreateIfNotExistsAsync(cancellationToken: ct);

        var blob = container.GetBlobClient(key);
        await using (var stream = file.OpenReadStream())
        {
            await blob.UploadAsync(
                stream,
                new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType } },
                cancellationToken: ct);
        }

        logger.LogInformation("Uploaded review image {Key} ({Bytes} bytes, {Type})",
            key, file.Length, file.ContentType);

        return Ok(new UploadedImage($"/api/images/{key}".ToNonEmpty()));
    }
}
