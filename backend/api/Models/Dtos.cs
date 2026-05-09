using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Api.Models;

// `required` policy — asymmetric on purpose:
//   * Request DTOs KEEP `required` so System.Text.Json throws on missing fields
//     at deserialization (runtime wire validation).
//   * Response DTOs DROP `required` and use `= default!` instead. Swashbuckle's
//     `NonNullableReferenceTypesAsRequired()` (Program.cs) still populates the
//     OpenAPI `required` array from C# nullability annotations.
//
// NonEmptyString policy — also asymmetric:
//   * Request DTOs use NonEmptyString where blank is invalid (the converter
//     rejects empty/whitespace at deserialization).
//   * Response DTOs only use it for values projected straight from EF entities
//     that already carry it. Synthesised values (workflow ids, blob URLs) stay
//     as plain `string`.

public record ProductSummary
{
    public long Id { get; init; }
    public NonEmptyString Slug { get; init; } = default!;
    public NonEmptyString Name { get; init; } = default!;
    public NonEmptyString? ImageUrl { get; init; }
    public double AverageRating { get; init; }
    public int ReviewCount { get; init; }
}

public record ProductDetail
{
    public long Id { get; init; }
    public NonEmptyString Slug { get; init; } = default!;
    public NonEmptyString Name { get; init; } = default!;
    public NonEmptyString Description { get; init; } = default!;
    public NonEmptyString? ImageUrl { get; init; }
    public double AverageRating { get; init; }
    public int ReviewCount { get; init; }
    public Guid? MyReviewId { get; init; }
}

public record ReviewItem
{
    public Guid Id { get; init; }
    public long ProductId { get; init; }
    public Guid AuthorId { get; init; }
    public NonEmptyString AuthorName { get; init; } = default!;
    public Rating Rating { get; init; }
    public NonEmptyString Title { get; init; } = default!;
    public NonEmptyString Body { get; init; } = default!;
    public IReadOnlyList<string> ImageUrls { get; init; } = Array.Empty<string>();
    public int Score { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public bool? MyVote { get; init; }
    public bool Mine { get; init; }
}

public record ReviewsPage
{
    public IReadOnlyList<ReviewItem> Items { get; init; } = Array.Empty<ReviewItem>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
}

// Type-level converter (not global) — a global JsonStringEnumConverter would
// shadow RatingJsonConverter and break the int 1..5 wire format for stars.
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ReviewSort
{
    Newest = 0,
    Helpful = 1,
    Highest = 2,
    Lowest = 3,
}

public record SubmitReviewRequest
{
    public required long ProductId { get; init; }
    public required Rating Rating { get; init; }
    public required NonEmptyString Title { get; init; }
    public required NonEmptyString Body { get; init; }
    public IReadOnlyList<NonEmptyString>? ImageUrls { get; init; }
    public required NonEmptyString TurnstileToken { get; init; }
}

public record EditReviewRequest
{
    public required Rating Rating { get; init; }
    public required NonEmptyString Title { get; init; }
    public required NonEmptyString Body { get; init; }
    public IReadOnlyList<NonEmptyString>? ImageUrls { get; init; }
}

public record VoteRequest
{
    public required bool IsUpvote { get; init; }
}

public record AcceptedResponse(string WorkflowId, string Status);

public record ConfigResponse(string TurnstileSiteKey);

public record UploadedImage(string Url);
