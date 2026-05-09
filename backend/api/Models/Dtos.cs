using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Api.Models;

// JSON shapes the API and SPA agree on. `record` for value-equality and terse
// declaration; StrongTypes wrappers carry intent into the payload.
//
// `required` policy — asymmetric on purpose:
//   * Request DTOs KEEP `required`. System.Text.Json honours it at deserialization
//     and throws JsonException when the wire payload omits the property — that's
//     the runtime missing-field validation we want at the wire boundary.
//   * Response DTOs DROP `required`. We construct these in C#; Swashbuckle is
//     configured with `NonNullableReferenceTypesAsRequired()` (Program.cs) so the
//     OpenAPI `required` array still gets populated from C# nullability annotations.
//     `= default!` initialisers on `init`-only properties suppress CS8618 without
//     forcing every call site into a positional record.
//
// NonEmptyString policy — also asymmetric:
//   * Request DTOs use NonEmptyString for fields that must be non-blank. The
//     wrapper's JsonConverter rejects empty / whitespace at deserialization
//     before the controller runs. That's its purpose.
//   * Response DTOs only use NonEmptyString when the value comes straight from
//     an EF entity that already carries it (e.g. ProductSummary.Slug). Fields we
//     synthesise in the controller (workflow ids, status strings, blob URLs)
//     stay as plain `string` — wrapping them via `.ToNonEmpty()` at the
//     construction site was ceremony with no validation benefit.

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
    // The user's existing review for this product, if any. Null when the
    // current viewer hasn't reviewed it. The SPA uses this to gate the
    // "Write a review" CTA into "Edit your review".
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
    // The current viewer's vote on this review (true = upvote, false =
    // downvote, null = no vote). Computed via a single LEFT JOIN at read
    // time; cheap enough that there's no point caching it.
    public bool? MyVote { get; init; }
    // True if the current viewer authored this review. Lets the SPA show
    // edit/delete actions on the user's own rows without leaking the
    // hashed AuthorId comparison logic to the client.
    public bool Mine { get; init; }
}

// Offset-based pagination — chosen for the reviews list because users on a
// reviews page expect "page 3 of 12", not an opaque cursor that loses position
// on refresh. TotalCount comes back so the SPA can render a real pager.
public record ReviewsPage
{
    public IReadOnlyList<ReviewItem> Items { get; init; } = Array.Empty<ReviewItem>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
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
// or to find the workflow in the Temporal UI for moderation. Positional
// record: small, constructed in one place per controller, no init-time
// initialisation noise.
public record AcceptedResponse(string WorkflowId, string Status);

public record ConfigResponse(string TurnstileSiteKey);

// Returned by POST /api/images — the public URL the SPA stores in the review's
// ImageUrls. Servers and clients both use the same `/api/images/...` shape.
public record UploadedImage(string Url);
