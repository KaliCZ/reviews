using Reviews.Infrastructure.Entities;
using StrongTypes;

namespace Reviews.Worker.Tests;

// Entity-level invariants. The Review ctor accepts NonEmptyString for the
// author / body / title-when-present, so empty strings are rejected at the
// type system level (they can't be constructed). Rating is a primitive
// short, so the range guard is checked separately.
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
            rating: 4,
            title: "Solid".ToNonEmpty(),
            body: "Tried it for a week, no complaints.".ToNonEmpty(),
            imageUrls: []);

        Assert.Equal("Alice", review.AuthorName.Value);
        Assert.Equal(ReviewStatus.Pending, review.Status);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)6)]
    [InlineData((short)-1)]
    [InlineData((short)100)]
    public void Rating_outside_one_to_five_is_rejected(short rating)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Review(
            id: Guid.NewGuid(),
            productId: 1,
            authorId: Guid.NewGuid(),
            authorName: "Alice".ToNonEmpty(),
            rating: rating,
            title: null,
            body: "Body".ToNonEmpty(),
            imageUrls: []));
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
    public void ApplyEdit_rejects_invalid_rating()
    {
        var review = new Review(
            id: Guid.NewGuid(),
            productId: 1,
            authorId: Guid.NewGuid(),
            authorName: "Alice".ToNonEmpty(),
            rating: 4,
            title: null,
            body: "Body".ToNonEmpty(),
            imageUrls: []);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            review.ApplyEdit(0, null, "Body".ToNonEmpty(), []));
    }
}
