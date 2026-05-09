using StrongTypes;

namespace Reviews.Api.Models;

// JSON shapes the API and SPA agree on. `record` for value-equality and terse
// declaration; StrongTypes wrappers carry intent into the payload — empty
// strings, missing required fields and rating overflows fail at deserialization
// before the controller runs (see the JsonConverter on each wrapper).

public record ProductSummary(
    long Id,
    NonEmptyString Slug,
    NonEmptyString Name,
    NonEmptyString? ImageUrl,
    double AverageRating,
    int ReviewCount);

public record ProductDetail(
    long Id,
    NonEmptyString Slug,
    NonEmptyString Name,
    NonEmptyString Description,
    NonEmptyString? ImageUrl,
    double AverageRating,
    int ReviewCount,
    // The user's existing review for this product, if any. Null when the
    // current viewer hasn't reviewed it. The SPA uses this to gate the
    // "Write a review" CTA into "Edit your review".
    Guid? MyReviewId);

public record ReviewItem(
    Guid Id,
    long ProductId,
    Guid AuthorId,
    NonEmptyString AuthorName,
    short Rating,
    NonEmptyString Title,
    NonEmptyString Body,
    IReadOnlyList<NonEmptyString> ImageUrls,
    int Score,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // The current viewer's vote on this review (+1 / -1 / null). Computed via
    // a single LEFT JOIN at read time; cheap enough that there's no point
    // caching it.
    short? MyVote,
    // True if the current viewer authored this review. Lets the SPA show
    // edit/delete actions on the user's own rows without leaking the
    // hashed AuthorId comparison logic to the client.
    bool Mine);

public record ReviewsPage(
    IReadOnlyList<ReviewItem> Items,
    string? NextCursor);

public record SubmitReviewRequest(
    long ProductId,
    short Rating,
    NonEmptyString Title,
    NonEmptyString Body,
    IReadOnlyList<NonEmptyString>? ImageUrls,
    // Cloudflare Turnstile token from the widget. Required in production;
    // dev uses Cloudflare's always-passes test keys.
    NonEmptyString TurnstileToken);

public record EditReviewRequest(
    short Rating,
    NonEmptyString Title,
    NonEmptyString Body,
    IReadOnlyList<NonEmptyString>? ImageUrls);

public record VoteRequest(short Value);

// Returned by mutation endpoints — the caller uses workflowId to poll status
// or to find the workflow in the Temporal UI for moderation.
public record AcceptedResponse(NonEmptyString WorkflowId, NonEmptyString Status);

public record ConfigResponse(NonEmptyString TurnstileSiteKey);

// Returned by POST /api/images — the public URL the SPA stores in the review's
// ImageUrls. Servers and clients both use the same `/api/images/...` shape.
public record UploadedImage(NonEmptyString Url);
