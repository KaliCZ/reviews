namespace Reviews.Shared;

// Shared task queue and activity names for the review workflows. Keeping them
// here lets the api and worker projects compile-time-agree on the strings
// without either project depending on the other's runtime types.
public static class ReviewQueues
{
    public const string TaskQueue = "reviews";
}

public static class ReviewActivityNames
{
    public const string PersistReview = "PersistReview";
    public const string ApproveReview = "ApproveReview";
    public const string RejectReview = "RejectReview";
    public const string LookupReview = "LookupReview";
    public const string ApplyReviewEdit = "ApplyReviewEdit";
    public const string SoftDeleteReview = "SoftDeleteReview";
    public const string UpsertVote = "UpsertVote";
    public const string InvalidateProductCaches = "InvalidateProductCaches";
}

// Signal-payload shared by every "needs human approval" workflow. Reason is
// optional so moderators can leave a note for the audit trail without it being
// required for an Approve.
public record ModerationDecision(bool Approved, string? Reason);

// Returned by LookupReview so the workflow can decide auto-apply vs moderation.
// Slug travels with it so the post-write cache invalidation can address its
// keys (which are slug-keyed, see ReviewsCacheKeys) without the workflow
// having to do its own DB lookup.
public record ReviewLookupResult(bool Found, bool OwnedByAuthor, long ProductId, string ProductSlug, DateTime CreatedAt);

// Returned by UpsertVote — null when the review didn't exist; otherwise the
// product slug whose caches now need invalidating.
public record VoteResult(bool ReviewFound, string ProductSlug);
