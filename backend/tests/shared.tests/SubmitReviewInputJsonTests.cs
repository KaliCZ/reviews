using System.Text.Json;
using Reviews.Infrastructure.Entities;
using Reviews.Shared;
using StrongTypes;

namespace Reviews.Shared.Tests;

// Wire-contract tests for the workflow inputs that cross the JSON boundary.
// `NonEmptyString` ships with a JSON converter that throws on empty / null
// / whitespace-only input, so any payload that violates the contract fails
// deserialization before it reaches an activity. These tests pin that
// behaviour: when the API or Temporal serialises inputs with the wrong
// shape, we want hard failures, not silent persistence of garbage.
public class SubmitReviewInputJsonTests
{
    // Mirror the API's JSON pipeline (Program.cs AddJsonOptions): respect
    // C#'s nullable annotations so missing / null fields on a non-nullable
    // record parameter throw JsonException, not bind silently.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    private static string ValidPayload(
        string body = "Looks great",
        string title = "Solid",
        string authorName = "Alice",
        string[]? imageUrls = null) =>
        $$"""
        {
            "reviewId": "11111111-1111-1111-1111-111111111111",
            "productId": 1,
            "authorId": "22222222-2222-2222-2222-222222222222",
            "authorName": {{JsonSerializer.Serialize(authorName)}},
            "rating": 4,
            "title": {{JsonSerializer.Serialize(title)}},
            "body": {{JsonSerializer.Serialize(body)}},
            "imageUrls": {{JsonSerializer.Serialize(imageUrls ?? Array.Empty<string>())}}
        }
        """;

    [Fact]
    public void Valid_payload_round_trips()
    {
        var input = JsonSerializer.Deserialize<SubmitReviewInput>(ValidPayload(), Json);
        Assert.NotNull(input);
        Assert.Equal("Looks great", input!.Body.Value);
        Assert.Equal("Alice", input.AuthorName.Value);
        Assert.Equal("Solid", input.Title.Value);
    }

    [Fact]
    public void Empty_body_is_rejected()
    {
        var json = ValidPayload(body: "");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewInput>(json, Json));
    }

    [Fact]
    public void Whitespace_only_body_is_rejected()
    {
        var json = ValidPayload(body: "   \t \n  ");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewInput>(json, Json));
    }

    [Fact]
    public void Empty_title_is_rejected()
    {
        var json = ValidPayload(title: "");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewInput>(json, Json));
    }

    [Fact]
    public void Whitespace_title_is_rejected()
    {
        var json = ValidPayload(title: "  \t\n  ");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewInput>(json, Json));
    }

    [Fact]
    public void Null_title_is_rejected()
    {
        // Title is required NonEmptyString — `null` on the wire violates the
        // contract just like an empty string would.
        var json = $$"""
        {
            "reviewId": "11111111-1111-1111-1111-111111111111",
            "productId": 1,
            "authorId": "22222222-2222-2222-2222-222222222222",
            "authorName": "Alice",
            "rating": 4,
            "title": null,
            "body": "Looks great",
            "imageUrls": []
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewInput>(json, Json));
    }

    [Fact]
    public void Empty_author_name_is_rejected()
    {
        var json = ValidPayload(authorName: "");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewInput>(json, Json));
    }

    [Fact]
    public void Empty_image_url_in_list_is_rejected()
    {
        var json = ValidPayload(imageUrls: ["/api/images/uploads/a.jpg", ""]);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SubmitReviewInput>(json, Json));
    }

    [Fact]
    public void Edit_input_rejects_empty_body()
    {
        var json = $$"""
        {
            "reviewId": "11111111-1111-1111-1111-111111111111",
            "authorId": "22222222-2222-2222-2222-222222222222",
            "rating": 3,
            "title": "Updated",
            "body": "",
            "imageUrls": []
        }
        """;
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EditReviewInput>(json, Json));
    }

    [Fact]
    public void NonEmptyString_factory_rejects_whitespace()
    {
        // Defence-in-depth check that the wrapper's contract still holds —
        // if this ever changes, the workflow inputs above silently widen.
        Assert.Null("   ".AsNonEmpty());
        Assert.Throws<ArgumentException>(() => "".ToNonEmpty());
        Assert.Equal("hi", "hi".ToNonEmpty().Value);
    }
}
