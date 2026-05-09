using StrongTypes;

namespace Reviews.Infrastructure.Entities;

// Explicit member values pin the on-disk encoding so reordering the C#
// declarations can't silently remap historical rows.
public enum ReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Deleted = 3,
}

// Writes flow through ApplyEdit/Approve/Reject/SoftDelete so audit fields
// (Status, UpdatedAtUtc) stay consistent. Private parameterless ctor is for
// EF materialization only.
public class Review
{
    private Review() { }

    public Review(
        Guid id,
        long productId,
        Guid authorId,
        NonEmptyString authorName,
        Rating rating,
        NonEmptyString title,
        NonEmptyString body,
        IReadOnlyList<NonEmptyString> imageUrls)
    {
        ArgumentNullException.ThrowIfNull(imageUrls);

        Id = id;
        ProductId = productId;
        AuthorId = authorId;
        AuthorName = authorName;
        Rating = rating;
        Title = title;
        Body = body;
        ImageUrls = imageUrls.Select(u => u.Value).ToList();
    }

    public Guid Id { get; private set; }
    public long ProductId { get; private set; }
    public Product Product { get; private set; } = null!;

    public Guid AuthorId { get; private set; }
    public NonEmptyString AuthorName { get; private set; } = null!;

    public Rating Rating { get; private set; }
    public NonEmptyString Title { get; private set; } = null!;
    public NonEmptyString Body { get; private set; } = null!;

    // Persisted as text[] (Npgsql maps List<string> natively); element-level
    // non-empty is enforced at the API boundary.
    public List<string> ImageUrls { get; private set; } = new List<string>();

    public ReviewStatus Status { get; private set; } = ReviewStatus.Pending;
    public int Score { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public ICollection<ReviewVote> Votes { get; private set; } = new List<ReviewVote>();

    // Doesn't change Status — Approved stays Approved, Pending stays Pending.
    public void ApplyEdit(Rating rating, NonEmptyString title, NonEmptyString body, IReadOnlyList<NonEmptyString> imageUrls)
    {
        ArgumentNullException.ThrowIfNull(imageUrls);

        Rating = rating;
        Title = title;
        Body = body;
        ImageUrls = imageUrls.Select(u => u.Value).ToList();
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Approve()
    {
        if (Status is ReviewStatus.Deleted)
            throw new InvalidOperationException("Cannot approve a deleted review");
        Status = ReviewStatus.Approved;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Reject()
    {
        if (Status is ReviewStatus.Deleted)
            throw new InvalidOperationException("Cannot reject a deleted review");
        Status = ReviewStatus.Rejected;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        Status = ReviewStatus.Deleted;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void RecordScore(int score)
    {
        Score = score;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    // Seed-only: pre-moderated, pre-scored, back-dated rows for first-boot UI.
    internal static Review CreateSeed(
        Guid id,
        long productId,
        Guid authorId,
        NonEmptyString authorName,
        Rating rating,
        NonEmptyString title,
        NonEmptyString body,
        IReadOnlyList<string> imageUrls,
        int score,
        ReviewStatus status,
        DateTime createdAt) =>
        new Review
        {
            Id = id,
            ProductId = productId,
            AuthorId = authorId,
            AuthorName = authorName,
            Rating = rating,
            Title = title,
            Body = body,
            ImageUrls = imageUrls.ToList(),
            Score = score,
            Status = status,
            CreatedAtUtc = createdAt,
            UpdatedAtUtc = createdAt,
        };
}
