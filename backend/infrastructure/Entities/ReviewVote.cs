namespace Reviews.Infrastructure.Entities;

public class ReviewVote
{
    public Guid ReviewId { get; set; }
    public Review Review { get; set; } = null!;

    // Voter is the OIDC `sub` claim mapped to a Guid (the Reviews user id).
    public Guid VoterId { get; set; }

    // +1 (helpful) or -1 (not helpful). CHECK constraint enforced by the
    // model configuration so no other value can land in the column.
    public short Value { get; set; }

    public DateTime CreatedAt { get; set; }
}
