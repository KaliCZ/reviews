namespace Reviews.Infrastructure.Entities;

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

    public Guid VoterId { get; private set; }

    public bool IsUpvote { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void Flip(bool isUpvote) => IsUpvote = isUpvote;

    public int ScoreContribution => IsUpvote ? 1 : -1;
}
