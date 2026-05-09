using StrongTypes;

namespace Reviews.Infrastructure.Entities;

// Persisted as integer (column type integer). Explicit member values pin the
// on-disk encoding so a future reorder of the C# declarations doesn't silently
// re-map historical rows — the rename of Approved is then a code-only change.
//
// Pending is the CLR default and the DB default. Newly-persisted reviews start
// here; only the temporal SubmitReviewWorkflow flips them to Approved (after
// either an immediate auto-approve for 3- and 4-star ratings, or a moderator
// signal for 1-, 2-, and 5-star).
public enum ReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Deleted = 3,
}

// Aggregate root for a single review of a product. Invariants enforced by:
//   - the public constructor (Rating is an enum so 1..5 is now a type-system
//     guarantee; non-empty body/author come from NonEmptyString),
//   - mutation methods (ApplyEdit / Approve / Reject / SoftDelete),
//   - the partial unique index in ReviewsDbContext (one live review per author).
//
// Properties have private setters so writes flow through the methods above —
// this keeps the audit fields (Status, UpdatedAtUtc) consistent without callers
// having to remember to bump UpdatedAtUtc themselves. The parameterless ctor is
// for EF Core materialization only; everything else goes through the public
// ctor or the internal seed factory.
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
        // Status defaults to Pending (CLR default of the enum). The temporal
        // submit workflow is the only path that flips it to Approved.
    }

    public Guid Id { get; private set; }
    public long ProductId { get; private set; }
    public Product Product { get; private set; } = null!;

    public Guid AuthorId { get; private set; }
    public NonEmptyString AuthorName { get; private set; } = null!;

    public Rating Rating { get; private set; }
    public NonEmptyString Title { get; private set; } = null!;
    public NonEmptyString Body { get; private set; } = null!;

    // Stored as Postgres text[]; EF Core's Npgsql provider maps List<string>
    // to text[] natively without a value converter. Element-level non-empty
    // is enforced at the API boundary; persisting plain strings keeps the
    // mapping straightforward (text[] vs. a NonEmptyString-element collection
    // would need a custom value comparer per-element).
    public List<string> ImageUrls { get; private set; } = new List<string>();

    public ReviewStatus Status { get; private set; } = ReviewStatus.Pending;
    public int Score { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public ICollection<ReviewVote> Votes { get; private set; } = new List<ReviewVote>();

    // Apply an author-driven edit. Doesn't change Status — an edit to an
    // already-Approved review stays Approved; an edit to a Pending one stays
    // Pending until the workflow signals through.
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

    // Set the denormalized score and bump UpdatedAtUtc. Called by the
    // RecordVote activity after recomputing the sum from review_votes —
    // keeping the mutation behind a method preserves the "writes flow through
    // methods, private setters" pattern the rest of the entity uses.
    public void RecordScore(int score)
    {
        Score = score;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    // Seeded demo reviews are pre-moderated, pre-scored, and back-dated so the
    // listing UI shows a populated catalog on first boot. Restricted to the
    // infrastructure assembly — production paths must go through the public
    // ctor + workflow.
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
