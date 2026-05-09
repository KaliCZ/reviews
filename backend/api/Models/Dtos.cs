using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Api.Models;

// JSON shapes the API and SPA agree on. `record` for value-equality and terse
// declaration; StrongTypes wrappers carry intent into the payload — empty
// strings, missing required fields and rating overflows fail at deserialization
// before the controller runs (see the JsonConverter on each wrapper).
//
// `required` on every property propagates through to the OpenAPI spec via
// Swashbuckle's built-in handling — the previous custom RequireNonNullable-
// SchemaFilter was redundant since C#'s `required` keyword already maps 1:1
// to the spec's `required` list, which the TS codegen reads. Use object-
// initializer syntax (`new ProductSummary { Id = …, Slug = …, … }`) at every
// call site so the compiler enforces that all required members are set.

public record ProductSummary
{
    public required long Id { get; init; }
    public required NonEmptyString Slug { get; init; }
    public required NonEmptyString Name { get; init; }
    public required NonEmptyString? ImageUrl { get; init; }
    public required double AverageRating { get; init; }
    public required int ReviewCount { get; init; }
}

public record ProductDetail
{
    public required long Id { get; init; }
    public required NonEmptyString Slug { get; init; }
    public required NonEmptyString Name { get; init; }
    public required NonEmptyString Description { get; init; }
    public required NonEmptyString? ImageUrl { get; init; }
    public required double AverageRating { get; init; }
    public required int ReviewCount { get; init; }
    // The user's existing review for this product, if any. Null when the
    // current viewer hasn't reviewed it. The SPA uses this to gate the
    // "Write a review" CTA into "Edit your review".
    public required Guid? MyReviewId { get; init; }
}

public record ReviewItem
{
    public required Guid Id { get; init; }
    public required long ProductId { get; init; }
    public required Guid AuthorId { get; init; }
    public required NonEmptyString AuthorName { get; init; }
    public required Rating Rating { get; init; }
    public required NonEmptyString Title { get; init; }
    public required NonEmptyString Body { get; init; }
    public required IReadOnlyList<NonEmptyString> ImageUrls { get; init; }
    public required int Score { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required DateTime UpdatedAt { get; init; }
    // The current viewer's vote on this review (true = upvote, false =
    // downvote, null = no vote). Computed via a single LEFT JOIN at read
    // time; cheap enough that there's no point caching it.
    public required bool? MyVote { get; init; }
    // True if the current viewer authored this review. Lets the SPA show
    // edit/delete actions on the user's own rows without leaking the
    // hashed AuthorId comparison logic to the client.
    public required bool Mine { get; init; }
}

// Offset-based pagination — chosen for the reviews list because users on a
// reviews page expect "page 3 of 12", not an opaque cursor that loses position
// on refresh. TotalCount comes back so the SPA can render a real pager.
public record ReviewsPage
{
    public required IReadOnlyList<ReviewItem> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
}

// Wire enum for the reviews listing sort key. Serialized as a string via
// type-level JsonStringEnumConverter — generated TS clients see a readable
// `'Newest' | 'Helpful' | 'Highest' | 'Lowest'` literal union. Reads are
// case-insensitive (the SPA can send 'newest' too).
//
// Type-level attribute (vs. global options.Converters) keeps Rating's
// integer wire format intact — registering JsonStringEnumConverter globally
// would override Rating's RatingJsonConverter and break the int-1..5
// contract the SPA relies on for star ratings.
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
    // Nullable: the SPA may send no array at all (no photos). An empty array
    // is also accepted. The element type is NonEmptyString so empty URLs
    // can't slip in.
    public IReadOnlyList<NonEmptyString>? ImageUrls { get; init; }
    // Cloudflare Turnstile token from the widget. Required in production;
    // dev uses Cloudflare's always-passes test keys.
    public required NonEmptyString TurnstileToken { get; init; }
}

public record EditReviewRequest
{
    public required Rating Rating { get; init; }
    public required NonEmptyString Title { get; init; }
    public required NonEmptyString Body { get; init; }
    public IReadOnlyList<NonEmptyString>? ImageUrls { get; init; }
}

// True = upvote, False = downvote. Boolean replaces the prior tri-state short
// (+1/-1) — captures the binary nature without a runtime range guard.
public record VoteRequest
{
    public required bool IsUpvote { get; init; }
}

// Returned by mutation endpoints — the caller uses workflowId to poll status
// or to find the workflow in the Temporal UI for moderation.
public record AcceptedResponse
{
    public required NonEmptyString WorkflowId { get; init; }
    public required NonEmptyString Status { get; init; }
}

public record ConfigResponse
{
    public required NonEmptyString TurnstileSiteKey { get; init; }
}

// Returned by POST /api/images — the public URL the SPA stores in the review's
// ImageUrls. Servers and clients both use the same `/api/images/...` shape.
public record UploadedImage
{
    public required NonEmptyString Url { get; init; }
}
