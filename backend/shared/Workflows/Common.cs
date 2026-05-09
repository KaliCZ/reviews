namespace Reviews.Shared;

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
    public const string RecordVote = "RecordVote";
    public const string InvalidateProductCaches = "InvalidateProductCaches";
}

public record ModerationDecision(bool Approved, string? Reason);

public record ReviewLookupResult(bool Found, bool OwnedByAuthor, long ProductId, string ProductSlug, DateTime CreatedAtUtc);

public record VoteResult(bool ReviewFound, string ProductSlug);
