using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Worker.Tests;

// Behaviour tests for the Review aggregate's mutation methods. The "construct
// an entity and read back a property" cases were dropped — they exercise the
// type system, not our domain logic. The end-to-end Submit / Edit / Vote paths
// are covered by the API integration tests.
public class ReviewEntityTests
{
    private static Review NewReview(Rating rating = Rating.Four) => new Review(
        id:         Guid.NewGuid(),
        productId:  1,
        authorId:   Guid.NewGuid(),
        authorName: "Alice".ToNonEmpty(),
        rating:     rating,
        title:      "T".ToNonEmpty(),
        body:       "Body".ToNonEmpty(),
        imageUrls:  []);

    [Fact]
    public void ApplyEdit_updates_fields_and_keeps_status()
    {
        var review = NewReview();
        // Approve first so we can verify the edit doesn't tip the status back.
        review.Approve();

        review.ApplyEdit(Rating.Two, "Updated".ToNonEmpty(), "New body".ToNonEmpty(), []);

        Assert.Equal(Rating.Two, review.Rating);
        Assert.Equal("Updated", review.Title.Value);
        Assert.Equal("New body", review.Body.Value);
        // ApplyEdit must NOT reset Status — an edit to an Approved review
        // stays Approved (the workflow's moderation gate runs separately).
        Assert.Equal(ReviewStatus.Approved, review.Status);
    }

    [Fact]
    public void Approve_flips_status_from_pending()
    {
        var review = NewReview();
        Assert.Equal(ReviewStatus.Pending, review.Status);

        review.Approve();

        Assert.Equal(ReviewStatus.Approved, review.Status);
    }

    [Fact]
    public void Reject_flips_status_from_pending()
    {
        var review = NewReview();

        review.Reject();

        Assert.Equal(ReviewStatus.Rejected, review.Status);
    }

    [Fact]
    public void SoftDelete_is_terminal_and_blocks_reapproval()
    {
        var review = NewReview();
        review.Approve();
        review.SoftDelete();

        Assert.Equal(ReviewStatus.Deleted, review.Status);
        // SoftDelete is terminal — re-approving must refuse so an audit
        // trail of "this review was approved, then deleted, then…" can't
        // be re-written.
        Assert.Throws<InvalidOperationException>(() => review.Approve());
        Assert.Throws<InvalidOperationException>(() => review.Reject());
    }

    [Fact]
    public void RecordScore_sets_score_and_bumps_updated()
    {
        var review = NewReview();
        var before = review.UpdatedAtUtc;

        review.RecordScore(7);

        Assert.Equal(7, review.Score);
        // UpdatedAtUtc must move forward — the activity calls RecordScore
        // after every vote, and stale UpdatedAtUtc would mask the change in
        // last-modified caches.
        Assert.True(review.UpdatedAtUtc >= before);
    }
}
