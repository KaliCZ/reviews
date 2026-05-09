using System.Text.Json;
using Reviews.Shared;

namespace Reviews.Shared.Tests;

// Wire-contract tests for the workflow inputs that cross the JSON boundary
// between the API and Temporal. Pin the round-trip shape; StrongTypes' own
// "empty NonEmptyString throws" behaviour is its package contract and isn't
// retested here.
public class SubmitReviewInputJsonTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
}
