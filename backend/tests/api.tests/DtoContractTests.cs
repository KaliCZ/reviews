using System.Text.Json;
using Reviews.Api.Models;
using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Api.Tests;

// Wire-shape tests on our DTOs — what the SPA / generated TS client see.
// Validation that "empty NonEmptyString throws JsonException" is StrongTypes'
// own contract and is not retested here; the end-to-end 400-response surface
// is exercised by InvalidJsonIntegrationTest.
public class DtoContractTests
{
    // Mirror the API's JSON pipeline (AddJsonOptions in Program.cs).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static string SubmitPayload(
        string body = "Looks great",
        string title = "Solid",
        string turnstile = "test-token",
        int rating = 4,
        string[]? imageUrls = null) =>
        $$"""
        {
            "productId": 1,
            "rating": {{rating}},
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
        Assert.Equal(Rating.Four, req.Rating);
        Assert.Null(req.ImageUrls);
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

    [Fact]
    public void ReviewItem_serialises_camelCase_Utc_suffixed_dates()
    {
        // Reviewer asked all stored DateTime properties be suffixed with
        // `Utc` so consumers can't ambiguously read them as local time. The
        // wire format follows: camelCase `createdAtUtc` / `updatedAtUtc`.
        var item = new ReviewItem
        {
            Id = Guid.NewGuid(),
            ProductId = 1,
            AuthorId = Guid.NewGuid(),
            AuthorName = "Alice".ToNonEmpty(),
            Rating = Rating.Four,
            Title = "T".ToNonEmpty(),
            Body = "B".ToNonEmpty(),
            ImageUrls = Array.Empty<string>(),
            Score = 0,
            CreatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc),
            MyVote = null,
            Mine = false,
        };
        var json = JsonSerializer.Serialize(item, Json);
        Assert.Contains("\"createdAtUtc\":", json);
        Assert.Contains("\"updatedAtUtc\":", json);
        Assert.DoesNotContain("\"createdAt\":", json);
        Assert.DoesNotContain("\"updatedAt\":", json);
    }

    [Fact]
    public void ReviewItem_round_trips_Utc_dates()
    {
        var original = new ReviewItem
        {
            Id = Guid.NewGuid(),
            ProductId = 1,
            AuthorId = Guid.NewGuid(),
            AuthorName = "Alice".ToNonEmpty(),
            Rating = Rating.Five,
            Title = "T".ToNonEmpty(),
            Body = "B".ToNonEmpty(),
            ImageUrls = new[] { "/api/images/uploads/a.jpg" },
            Score = 7,
            CreatedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc),
            MyVote = true,
            Mine = true,
        };
        var json = JsonSerializer.Serialize(original, Json);
        var back = JsonSerializer.Deserialize<ReviewItem>(json, Json);
        Assert.NotNull(back);
        Assert.Equal(original.CreatedAtUtc, back!.CreatedAtUtc);
        Assert.Equal(original.UpdatedAtUtc, back.UpdatedAtUtc);
    }

    [Fact]
    public void AcceptedResponse_constructs_with_plain_strings()
    {
        // Response DTOs dropped `required` and switched off NonEmptyString
        // for fields the controller synthesises (workflow id, status). The
        // positional record makes the construction site terse — verify it
        // round-trips so the SPA still sees the camelCase shape it expects.
        var resp = new AcceptedResponse("submit-review-abc", "submitted");
        var json = JsonSerializer.Serialize(resp, Json);
        Assert.Contains("\"workflowId\":\"submit-review-abc\"", json);
        Assert.Contains("\"status\":\"submitted\"", json);
    }

    [Fact]
    public void Response_DTOs_allow_object_initializer_without_required()
    {
        // `required` is dropped from response DTOs because Swashbuckle
        // infers `required` in OpenAPI via NonNullableReferenceTypesAsRequired.
        // The compiler should let an object initializer omit fields without
        // CS9035 (the diagnostic for missing required member). This test
        // primarily exists to fail at compile time if `required` slips back
        // onto a response DTO — the assertion is incidental.
        var summary = new ProductSummary
        {
            Id = 1,
            Slug = "foo".ToNonEmpty(),
            Name = "Foo".ToNonEmpty(),
            ImageUrl = null,
            AverageRating = 0,
            ReviewCount = 0,
        };
        Assert.Equal(1, summary.Id);
    }
}
