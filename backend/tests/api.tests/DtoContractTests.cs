using System.Text.Json;
using System.Text.Json.Serialization;
using Reviews.Api.Models;
using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Api.Tests;

// JSON-level negative tests on the public API request DTOs. Each property
// typed as NonEmptyString rejects empty / whitespace-only strings during
// deserialization, so the controller never sees an invalid payload — the
// 400 response comes from ASP.NET's built-in BadHttpRequestException
// translation of JsonException.
//
// `required` on record members is enforced by STJ since .NET 7 — missing
// fields throw JsonException without any additional opt-in.
public class DtoContractTests
{
    // Mirror the API's JSON pipeline (AddJsonOptions in Program.cs). Without
    // RespectNullableAnnotations, STJ silently binds `null` / missing
    // properties into non-nullable members.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        // Enum converters are pinned at the type level — Rating uses
        // RatingJsonConverter (int 1..5), ReviewSort uses
        // JsonStringEnumConverter. Adding a global JsonStringEnumConverter
        // here would override the Rating attribute (options.Converters
        // outranks type-level [JsonConverter]) and break the int wire
        // contract the SPA relies on for star ratings.
    };

    private static string SubmitPayload(
        string body = "Looks great",
        string title = "Solid",
        string turnstile = "test-token",
        int rating = 4,
        string[]? imageUrls = null,
        string language = "en") =>
        $$"""
        {
            "productId": 1,
            "rating": {{rating}},
            "title": {{JsonSerializer.Serialize(title)}},
            "body": {{JsonSerializer.Serialize(body)}},
            "imageUrls": {{(imageUrls is null ? "null" : JsonSerializer.Serialize(imageUrls))}},
            "language": {{JsonSerializer.Serialize(language)}},
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
        Assert.Equal(Rating.Four, req.Rating);
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
        // exactly like an empty string would.
        var json = """
        {
            "productId": 1,
            "rating": 4,
            "title": null,
            "body": "Looks great",
            "language": "en",
            "turnstileToken": "test-token"
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewRequest>(json, Json));
    }

    [Fact]
    public void Submit_missing_required_field_is_rejected()
    {
        // Title is `required` — omitting it from the payload entirely throws,
        // not just sending `null`. Pins down the C# `required` semantics.
        var json = """
        {
            "productId": 1,
            "rating": 4,
            "body": "Looks great",
            "language": "en",
            "turnstileToken": "test-token"
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewRequest>(json, Json));
    }

    [Fact]
    public void Submit_missing_language_is_rejected()
    {
        // Language is `required` so the payload always carries the wire
        // language tag. Drives the per-review translate UX — without a
        // value we can't gate it.
        var json = """
        {
            "productId": 1,
            "rating": 4,
            "title": "Solid",
            "body": "Looks great",
            "turnstileToken": "test-token"
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewRequest>(json, Json));
    }

    [Fact]
    public void Submit_with_empty_language_is_rejected()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(language: ""), Json));
    }

    [Fact]
    public void Submit_round_trips_language_tag()
    {
        var req = JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(language: "cs"), Json);
        Assert.NotNull(req);
        Assert.Equal("cs", req!.Language.Value);
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
        // The SPA always sends `imageUrls` (possibly empty) — an empty array
        // is the no-photos representation on the wire (the property itself is
        // optional, so missing is also valid).
        var req = JsonSerializer.Deserialize<SubmitReviewRequest>(
            SubmitPayload(imageUrls: []), Json);
        Assert.NotNull(req);
        Assert.Empty(req!.ImageUrls!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Submit_with_out_of_range_rating_is_rejected(int rating)
    {
        // RatingJsonConverter enforces 1..5 at the wire layer — out-of-range
        // ints fail at deserialization, before the controller runs. No
        // controller needs an "is the rating between 1 and 5" check anywhere.
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(rating: rating), Json));
    }

    [Theory]
    [InlineData(1, Rating.One)]
    [InlineData(2, Rating.Two)]
    [InlineData(3, Rating.Three)]
    [InlineData(4, Rating.Four)]
    [InlineData(5, Rating.Five)]
    public void Submit_accepts_valid_integer_ratings(int wire, Rating expected)
    {
        var req = JsonSerializer.Deserialize<SubmitReviewRequest>(SubmitPayload(rating: wire), Json);
        Assert.Equal(expected, req!.Rating);
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
        // Title is required — omitting the property is a hard reject.
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
    public void Vote_request_carries_boolean()
    {
        // Replaced the prior tri-state short (+1/-1) with a bool — true =
        // upvote, false = downvote. Captures the binary nature without a
        // runtime range guard.
        var up = JsonSerializer.Deserialize<VoteRequest>("""{ "isUpvote": true }""", Json);
        Assert.NotNull(up);
        Assert.True(up!.IsUpvote);

        var down = JsonSerializer.Deserialize<VoteRequest>("""{ "isUpvote": false }""", Json);
        Assert.NotNull(down);
        Assert.False(down!.IsUpvote);
    }

    [Fact]
    public void Vote_missing_isUpvote_is_rejected()
    {
        // `required bool IsUpvote` — omitting the field throws, not silently
        // defaulting to false (which would mean "downvote", an actual action).
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<VoteRequest>("""{}""", Json));
    }

    [Fact]
    public void ReviewSort_round_trips_named_strings()
    {
        // Generated TS clients see a string union — pin the names so a
        // future enum reorder doesn't silently break the wire contract.
        // JsonStringEnumConverter with default naming uses the C# member
        // name (PascalCase). The SPA matches on these names.
        var sort = JsonSerializer.Deserialize<ReviewSort>("\"Helpful\"", Json);
        Assert.Equal(ReviewSort.Helpful, sort);
        sort = JsonSerializer.Deserialize<ReviewSort>("\"helpful\"", Json);
        // Default JsonStringEnumConverter is case-insensitive on read.
        Assert.Equal(ReviewSort.Helpful, sort);

        var serialised = JsonSerializer.Serialize(ReviewSort.Lowest, Json);
        Assert.Equal("\"Lowest\"", serialised);
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
