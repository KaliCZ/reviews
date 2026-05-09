using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Worker.Tests;

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
        review.Approve();

        review.ApplyEdit(Rating.Two, "Updated".ToNonEmpty(), "New body".ToNonEmpty(), []);

        Assert.Equal(Rating.Two, review.Rating);
        Assert.Equal("Updated", review.Title.Value);
        Assert.Equal("New body", review.Body.Value);
        // ApplyEdit must NOT reset Status — moderation runs separately in the workflow.
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
        // SoftDelete is terminal so the audit trail can't be rewritten.
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
        Assert.True(review.UpdatedAtUtc >= before);
    }
}
