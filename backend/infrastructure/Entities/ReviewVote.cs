namespace Reviews.Infrastructure.Entities;

// Single vote by a single user on a single review. The composite (ReviewId,
// VoterId) primary key enforces "one vote per user per review" at the storage
// layer; the boolean IsUpvote is the only payload (true = upvote / +1,
// false = downvote / -1), so the prior CHECK on a tri-state smallint goes away
// — the type system carries the constraint.
//
// The vote workflow id is now deterministic per (review, voter) and joins an
// in-flight execution via WorkflowIdConflictPolicy.UseExisting, so the activity
// is the only writer per (review, voter) at any moment — fetch-or-create on the
// row is safe and EF-tracked, no UPSERT needed.
public class ReviewVote
{
    private ReviewVote() { }

    public ReviewVote(Guid reviewId, Guid voterId, bool isUpvote)
    {
        ReviewId = reviewId;
        VoterId = voterId;
        IsUpvote = isUpvote;
    }

    public Guid ReviewId { get; private set; }
    public Review Review { get; private set; } = null!;

    // Voter is the OIDC `sub` claim mapped to a Guid (the Reviews user id).
    public Guid VoterId { get; private set; }

    public bool IsUpvote { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void Flip(bool isUpvote) => IsUpvote = isUpvote;

    // The denormalized Review.Score is sum(IsUpvote ? 1 : -1) — exposed here
    // so the score-recompute path has a single source of truth for the
    // bool→signed-score translation.
    public int ScoreContribution => IsUpvote ? 1 : -1;
}
