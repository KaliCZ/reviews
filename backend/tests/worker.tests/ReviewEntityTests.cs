using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Worker.Tests;

// Entity-level invariants. The Review ctor takes NonEmptyString for the
// author / title / body, so empty strings are rejected at the type system
// level (they can't be constructed). Rating is now an enum (1..5 by member),
// so the prior runtime range guard is gone — the type carries the constraint.
public class ReviewEntityTests
{
    [Fact]
    public void Ctor_accepts_valid_input()
    {
        var review = new Review(
            id: Guid.NewGuid(),
            productId: 1,
            authorId: Guid.NewGuid(),
            authorName: "Alice".ToNonEmpty(),
            rating: Rating.Four,
            title: "Solid".ToNonEmpty(),
            body: "Tried it for a week, no complaints.".ToNonEmpty(),
            imageUrls: [],
            language: "en".ToNonEmpty());

        Assert.Equal("Alice", review.AuthorName.Value);
        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Equal(Rating.Four, review.Rating);
    }

    [Fact]
    public void Empty_body_is_a_compile_or_runtime_error()
    {
        // The StrongTypes contract: ToNonEmpty throws for empty input. The
        // Review ctor parameter is NonEmptyString, so the failure is at the
        // call site, not deep inside the entity.
        Assert.Throws<ArgumentException>(() => "".ToNonEmpty());
    }

    [Fact]
    public void ApplyEdit_updates_fields()
    {
        var review = new Review(
            id: Guid.NewGuid(),
            productId: 1,
            authorId: Guid.NewGuid(),
            authorName: "Alice".ToNonEmpty(),
            rating: Rating.Four,
            title: "T".ToNonEmpty(),
            body: "Body".ToNonEmpty(),
            imageUrls: [],
            language: "en".ToNonEmpty());

        review.ApplyEdit(Rating.Two, "Updated".ToNonEmpty(), "New body".ToNonEmpty(), [], "cs".ToNonEmpty());

        Assert.Equal(Rating.Two, review.Rating);
        Assert.Equal("Updated", review.Title.Value);
        Assert.Equal("New body", review.Body.Value);
        Assert.Equal("cs", review.Language.Value);
    }

    [Fact]
    public void Approve_then_soft_delete_then_approve_throws()
    {
        var review = new Review(
            id: Guid.NewGuid(),
            productId: 1,
            authorId: Guid.NewGuid(),
            authorName: "Alice".ToNonEmpty(),
            rating: Rating.Five,
            title: "T".ToNonEmpty(),
            body: "Body".ToNonEmpty(),
            imageUrls: [],
            language: "en".ToNonEmpty());

        review.Approve();
        Assert.Equal(ReviewStatus.Approved, review.Status);

        review.SoftDelete();
        Assert.Equal(ReviewStatus.Deleted, review.Status);

        // SoftDelete is terminal — re-approving should refuse so an audit
        // trail of "this review was approved, then deleted, then…" can't be
        // re-written.
        Assert.Throws<InvalidOperationException>(() => review.Approve());
    }

    [Fact]
    public void Vote_records_signed_score_contribution()
    {
        // ReviewVote is a boolean (true=upvote, false=downvote); the entity
        // exposes ScoreContribution so the score-recompute path doesn't have
        // to know the bool→±1 mapping.
        var up = new ReviewVote(Guid.NewGuid(), Guid.NewGuid(), isUpvote: true);
        Assert.Equal(1, up.ScoreContribution);

        var down = new ReviewVote(Guid.NewGuid(), Guid.NewGuid(), isUpvote: false);
        Assert.Equal(-1, down.ScoreContribution);

        // Flip is the runtime-mutating path used by the activity when a
        // user changes their vote (the SQL UPSERT is the storage hot path
        // but tests pin the entity behaviour for any future EF-tracking
        // callers).
        up.Flip(false);
        Assert.False(up.IsUpvote);
        Assert.Equal(-1, up.ScoreContribution);
    }
}
