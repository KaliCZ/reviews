using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reviews.Api.Models;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;
using StrongTypes;

namespace Reviews.Api.Controllers;

// Streams blobs through the API so the browser-visible URL stays the same
// across dev/compose/prod regardless of how blob storage is reached internally.
// TODO: replace this passthrough with a CDN.
[ApiController]
[Route("api/[controller]")]
public class ImagesController(BlobServiceClient blobs, ILogger<ImagesController> logger) : ControllerBase
{
    public const long MaxImageBytes = 2L * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
            // Immutable: blob keys are content-addressed (review edits get new keys).
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return File(stream, props.Value.ContentType ?? "image/jpeg");
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return NotFound();
        }
    }

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

        var ext = ExtensionFor(file.ContentType);
        var key = $"uploads/{Sequential.NewGuid():N}{ext}";

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

        return Ok(new UploadedImage($"/api/images/{key}"));
    }

    private static string ExtensionFor(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        _ => "",
    };
}
