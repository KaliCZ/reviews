using System.Text.Json;
using Reviews.Api.Models;
using StrongTypes;

namespace Reviews.Api.Tests;

// JSON-level negative tests on the public API request DTOs. Each property
// typed as NonEmptyString rejects empty / whitespace-only strings during
// deserialization, so the controller never sees an invalid payload — the
// 400 response comes from ASP.NET's built-in BadHttpRequestException
// translation of JsonException.
public class DtoContractTests
{
    // Mirror the API's JSON pipeline (AddJsonOptions in Program.cs). Without
    // RespectNullableAnnotations, STJ silently binds `null` / missing
    // properties into non-nullable record parameters; with it, they throw
    // JsonException on the wire — same shape the controller sees.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private static string SubmitPayload(
        string body = "Looks great",
        string title = "Solid",
        string turnstile = "test-token",
        string[]? imageUrls = null) =>
        $$"""
        {
            "productId": 1,
            "rating": 4,
            "title": {{JsonSerializer.Serialize(title)}},
            "body": {{JsonSerializer.Serialize(body)}},
            "imageUrls": {{(imageUrls is null ? "null" : JsonSerializer.Serialize(imageUrls))}},
            "turnstileToken": {{JsonSerializer.Serialize(turnstile)}}
        }
        """;

    [Fact]
    public void Valid_submit_payload_round_trips()
    {
        var req = JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(), Json);
        Assert.NotNull(req);
        Assert.Equal("Solid", req!.Title.Value);
        Assert.Equal("Looks great", req.Body.Value);
        Assert.Equal("test-token", req.TurnstileToken.Value);
        Assert.Null(req.ImageUrls);
    }

    [Fact]
    public void Submit_with_empty_body_is_rejected()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(body: ""), Json));
    }

    [Fact]
    public void Submit_with_whitespace_body_is_rejected()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(body: " \t \n"), Json));
    }

    [Fact]
    public void Submit_with_empty_title_is_rejected()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(title: ""), Json));
    }

    [Fact]
    public void Submit_with_whitespace_title_is_rejected()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(title: "   "), Json));
    }

    [Fact]
    public void Submit_with_null_title_is_rejected()
    {
        // Title is required NonEmptyString — null violates the contract
        // exactly like an empty string. `null` triggers the converter just
        // the same as a too-short value would.
        var json = """
        {
            "productId": 1,
            "rating": 4,
            "title": null,
            "body": "Looks great",
            "turnstileToken": "test-token"
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewRequest>(json, Json));
    }

    [Fact]
    public void Submit_with_empty_turnstile_token_is_rejected()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(turnstile: ""), Json));
    }

    [Fact]
    public void Submit_with_empty_image_url_is_rejected()
    {
        var json = SubmitPayload(imageUrls: ["/api/images/uploads/a.jpg", ""]);
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitReviewRequest>(json, Json));
    }

    [Fact]
    public void Submit_with_empty_image_urls_array_round_trips()
    {
        // The SPA always sends `imageUrls` (possibly empty) — RespectRequired-
        // ConstructorParameters would reject a missing key, so this pins down
        // that an empty array is the no-photos representation on the wire.
        var req = JsonSerializer.Deserialize<SubmitReviewRequest>(
            SubmitPayload(imageUrls: []), Json);
        Assert.NotNull(req);
        Assert.Empty(req!.ImageUrls!);
    }

    [Fact]
    public void Edit_with_empty_body_is_rejected()
    {
        const string json = """
        {
            "rating": 3,
            "title": "Updated title",
            "body": "",
            "imageUrls": []
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EditReviewRequest>(json, Json));
    }

    [Fact]
    public void Edit_with_whitespace_title_is_rejected()
    {
        const string json = """
        {
            "rating": 3,
            "title": "   ",
            "body": "Updated body",
            "imageUrls": []
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EditReviewRequest>(json, Json));
    }

    [Fact]
    public void Edit_with_missing_title_is_rejected()
    {
        // Title is required — omitting the property leaves it null, which
        // the NonEmptyString converter rejects.
        const string json = """
        {
            "rating": 3,
            "body": "Updated body",
            "imageUrls": []
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EditReviewRequest>(json, Json));
    }

    [Fact]
    public void Vote_request_carries_only_value()
    {
        // VoteRequest is a primitive `short` — controller-level validation
        // rejects values outside ±1, but the JSON layer doesn't constrain.
        var ok = JsonSerializer.Deserialize<VoteRequest>("""{ "value": 1 }""", Json);
        Assert.NotNull(ok);
        Assert.Equal((short)1, ok!.Value);

        // Bogus values still parse — caller's responsibility.
        var bogus = JsonSerializer.Deserialize<VoteRequest>("""{ "value": 7 }""", Json);
        Assert.NotNull(bogus);
        Assert.Equal((short)7, bogus!.Value);
    }

    [Fact]
    public void Image_upload_constants_match_documented_limits()
    {
        // The 2 MiB cap referenced in the SPA's `Limits.maxImageBytes` and
        // the docs is wired to ImagesController.MaxImageBytes — pin the
        // value so a future tweak shows up in test diffs across both sides.
        Assert.Equal(2L * 1024 * 1024, Reviews.Api.Controllers.ImagesController.MaxImageBytes);
    }
}
