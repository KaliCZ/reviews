namespace Reviews.Infrastructure.Entities;

public enum ReviewStatus
{
    // Persisted as text via a value converter — the workflow gate before
    // persist means we mostly only ever see Approved here. Pending/Rejected
    // exist for future flows (e.g. drafts, hidden-but-retained moderation).
    Pending,
    Approved,
    Rejected,
    Deleted
}

public class Review
{
    public Guid Id { get; set; }
    public long ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;

    public short Rating { get; set; }
    public string? Title { get; set; }
    public string Body { get; set; } = string.Empty;

    // Stored as Postgres text[]; EF Core's Npgsql provider maps List<string>
    // to text[] natively without a value converter.
    public List<string> ImageUrls { get; set; } = new();

    public ReviewStatus Status { get; set; } = ReviewStatus.Approved;
    public int Score { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<ReviewVote> Votes { get; set; } = new List<ReviewVote>();
}
