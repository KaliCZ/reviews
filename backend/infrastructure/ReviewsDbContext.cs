using Microsoft.EntityFrameworkCore;
using Reviews.Infrastructure.Entities;

namespace Reviews.Infrastructure;

public class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : DbContext(options)
{
    public const string Schema = "reviews";

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewVote> ReviewVotes => Set<ReviewVote>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<Product>(e =>
        {
            e.ToTable("products");
            e.HasKey(p => p.Id);
            // Matches docs/flows.md: product IDs are int64 and provided by the
            // upstream catalog (this service doesn't generate them).
            e.Property(p => p.Id).ValueGeneratedNever();
            e.Property(p => p.Slug).IsRequired();
            e.HasIndex(p => p.Slug).IsUnique();
            e.Property(p => p.Name).IsRequired();
            e.Property(p => p.Description).IsRequired();
            e.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");
        });

        b.Entity<Review>(e =>
        {
            e.ToTable("reviews");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(r => r.AuthorName).IsRequired();
            e.Property(r => r.Body).IsRequired();
            e.Property(r => r.ImageUrls).HasColumnType("text[]");
            e.Property(r => r.Rating);
            // CHECK constraints reference column names verbatim — quote
            // PascalCase identifiers so Postgres treats them case-sensitively
            // the way EF Core stores them.
            e.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "\"Rating\" BETWEEN 1 AND 5"));

            // Status is persisted as integer (the default for enums in EF Core
            // — explicit member values are pinned in ReviewStatus.cs). Pending
            // is both the CLR default *and* the DB default; HasSentinel(Pending)
            // tells EF that "the property is at the CLR default" means "let the
            // DB default kick in" rather than "I forgot to set it" — silences
            // the no-sentinel warning. CHECK guards against bogus int values
            // sneaking in via raw SQL.
            e.Property(r => r.Status)
                .HasDefaultValue(ReviewStatus.Pending)
                .HasSentinel(ReviewStatus.Pending);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_reviews_status",
                $"\"Status\" BETWEEN {(int)ReviewStatus.Pending} AND {(int)ReviewStatus.Deleted}"));

            e.Property(r => r.Score).HasDefaultValue(0);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(r => r.UpdatedAt).HasDefaultValueSql("NOW()");

            e.HasOne(r => r.Product)
                .WithMany(p => p.Reviews)
                .HasForeignKey(r => r.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            // One live review per (product, author). The partial index ignores
            // soft-deleted rows so a user who deletes their review can re-post.
            // Status filter compares against the integer encoding of Deleted (3)
            // rather than the prior text 'Deleted' literal.
            e.HasIndex(r => new { r.ProductId, r.AuthorId })
                .IsUnique()
                .HasFilter($"\"Status\" <> {(int)ReviewStatus.Deleted}")
                .HasDatabaseName("uq_reviews_product_author");

            // Sort indexes for keyset pagination. Each carries the (id)
            // tiebreaker so pagination is fully deterministic. Filtered to
            // Approved (int 1) so they stay tight (no pending/rejected/deleted
            // noise).
            var approvedFilter = $"\"Status\" = {(int)ReviewStatus.Approved}";
            e.HasIndex(r => new { r.ProductId, r.CreatedAt, r.Id })
                .HasDatabaseName("idx_reviews_newest")
                .HasFilter(approvedFilter)
                .IsDescending(false, true, true);
            e.HasIndex(r => new { r.ProductId, r.Score, r.Id })
                .HasDatabaseName("idx_reviews_helpful")
                .HasFilter(approvedFilter)
                .IsDescending(false, true, true);
            e.HasIndex(r => new { r.ProductId, r.Rating, r.CreatedAt, r.Id })
                .HasDatabaseName("idx_reviews_rating")
                .HasFilter(approvedFilter)
                .IsDescending(false, true, true, true);
        });

        b.Entity<ReviewVote>(e =>
        {
            e.ToTable("review_votes");
            // Composite PK (review_id, voter_id) is what makes flip-vote a
            // single UPSERT and prevents double-voting at the storage layer.
            e.HasKey(v => new { v.ReviewId, v.VoterId });
            e.Property(v => v.CreatedAt).HasDefaultValueSql("NOW()");
            e.ToTable(t => t.HasCheckConstraint("ck_review_votes_value", "\"Value\" IN (-1, 1)"));

            e.HasOne(v => v.Review)
                .WithMany(r => r.Votes)
                .HasForeignKey(v => v.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
