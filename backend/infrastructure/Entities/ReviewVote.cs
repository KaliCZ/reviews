namespace Reviews.Infrastructure.Entities;

// Single vote by a single user on a single review. The composite (ReviewId,
// VoterId) primary key + a CHECK on Value enforces "one vote per user per
// review, must be +1 or -1" at the storage layer.
//
// Note: the runtime hot path UPSERTs ReviewVote rows via raw SQL inside
// ReviewActivities.UpsertVoteAsync (cheaper than EF tracking for a single
// statement upsert). This entity exists for schema definition + materialization
// on read paths, with the same constructor invariants as the SQL CHECK.
public class ReviewVote
{
    private ReviewVote() { }

    public ReviewVote(Guid reviewId, Guid voterId, short value)
    {
        if (value is not (1 or -1))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Vote value must be +1 or -1");

        ReviewId = reviewId;
        VoterId = voterId;
        Value = value;
    }

    public Guid ReviewId { get; private set; }
    public Review Review { get; private set; } = null!;

    // Voter is the OIDC `sub` claim mapped to a Guid (the Reviews user id).
    public Guid VoterId { get; private set; }

    public short Value { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public void ChangeValue(short value)
    {
        if (value is not (1 or -1))
            throw new ArgumentOutOfRangeException(nameof(value), value, "Vote value must be +1 or -1");
        Value = value;
    }
}
