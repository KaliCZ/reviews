using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Api.Models;

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
    // Approved for everything in the shared listing; only the per-viewer
    // MyReview overlay carries Pending or Rejected.
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public ReviewStatus Status { get; init; } = ReviewStatus.Approved;
}

public record ReviewsPage
{
    public IReadOnlyList<ReviewItem> Items { get; init; } = Array.Empty<ReviewItem>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    // The viewer's own review for the product (any non-Deleted status), so
    // they see it immediately after submitting even while it's Pending or
    // before the cached page is invalidated. Filtered out of `Items` to
    // avoid duplication. Null for anonymous viewers or no own review.
    public ReviewItem? MyReview { get; init; }
}

// Type-level converter (not global) — a global JsonStringEnumConverter would
// shadow RatingJsonConverter and break the int 1..5 wire format for stars.
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum ReviewSort
{
    Date = 0,
    Helpful = 1,
    Rating = 2,
}

[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum SortDirection
{
    Desc = 0,
    Asc = 1,
}

public record SubmitReviewRequest
{
    public long ProductId { get; init; }
    public Rating Rating { get; init; }
    public NonEmptyString Title { get; init; } = default!;
    public NonEmptyString Body { get; init; } = default!;
    public IReadOnlyList<NonEmptyString>? ImageUrls { get; init; }
    public NonEmptyString TurnstileToken { get; init; } = default!;
}

public record EditReviewRequest
{
    public Rating Rating { get; init; }
    public NonEmptyString Title { get; init; } = default!;
    public NonEmptyString Body { get; init; } = default!;
    public IReadOnlyList<NonEmptyString>? ImageUrls { get; init; }
}

public record VoteRequest
{
    public bool IsUpvote { get; init; }
}

public record AcceptedResponse(string WorkflowId, string Status);

public record VoteResponse(int Score, bool? MyVote);

public record ConfigResponse(string TurnstileSiteKey);

public record UploadedImage(string Url);
