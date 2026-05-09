using Reviews.Infrastructure.Entities;

namespace Reviews.Worker.Tests;

// Dedicated entity-level tests for ReviewVote. The RecordVote activity now
// uses EF tracking + Flip() instead of raw UPSERT, so the entity behaviour
// is on the runtime hot path (not just the seed/read path). Pin the
// invariants so a future tweak shows up in test diffs.
public class ReviewVoteTests
{
    private static readonly Guid AReview = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AVoter  = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Ctor_sets_isUpvote_true()
    {
        var vote = new ReviewVote(AReview, AVoter, isUpvote: true);
        Assert.True(vote.IsUpvote);
        Assert.Equal(AReview, vote.ReviewId);
        Assert.Equal(AVoter, vote.VoterId);
    }

    [Fact]
    public void Ctor_sets_isUpvote_false()
    {
        var vote = new ReviewVote(AReview, AVoter, isUpvote: false);
        Assert.False(vote.IsUpvote);
    }

    [Fact]
    public void Flip_changes_isUpvote()
    {
        var vote = new ReviewVote(AReview, AVoter, isUpvote: true);
        vote.Flip(false);
        Assert.False(vote.IsUpvote);
        vote.Flip(true);
        Assert.True(vote.IsUpvote);
    }

    [Fact]
    public void Flip_with_same_value_is_idempotent()
    {
        // Calling Flip with the current value is a harmless no-op — the
        // RecordVote activity calls it unconditionally on the existing row,
        // so the same-value case is the rapid-double-click path.
        var vote = new ReviewVote(AReview, AVoter, isUpvote: true);
        vote.Flip(true);
        Assert.True(vote.IsUpvote);
        vote.Flip(true);
        Assert.True(vote.IsUpvote);
    }

    [Fact]
    public void ScoreContribution_is_signed_one()
    {
        // The score column on Review is sum(IsUpvote ? 1 : -1). Exposed on
        // the entity so the recompute path doesn't have to know the
        // bool→±1 mapping.
        Assert.Equal(1, new ReviewVote(AReview, AVoter, isUpvote: true).ScoreContribution);
        Assert.Equal(-1, new ReviewVote(AReview, AVoter, isUpvote: false).ScoreContribution);
    }

    [Fact]
    public void ScoreContribution_follows_flip()
    {
        var vote = new ReviewVote(AReview, AVoter, isUpvote: true);
        Assert.Equal(1, vote.ScoreContribution);
        vote.Flip(false);
        Assert.Equal(-1, vote.ScoreContribution);
    }
}
