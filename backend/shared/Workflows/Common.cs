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
    public const string LookupReview = "LookupReview";
    public const string ApplyReviewEdit = "ApplyReviewEdit";
    public const string SoftDeleteReview = "SoftDeleteReview";
    public const string UpsertVote = "UpsertVote";
    public const string RefreshFirstPageCache = "RefreshFirstPageCache";
}

// Signal-payload shared by every "needs human approval" workflow. Reason is
// optional so moderators can leave a note for the audit trail without it being
// required for an Approve.
public record ModerationDecision(bool Approved, string? Reason);

// Returned by LookupReview so the workflow can decide auto-apply vs moderation.
public record ReviewLookupResult(bool Found, bool OwnedByAuthor, long ProductId, DateTime CreatedAt);
