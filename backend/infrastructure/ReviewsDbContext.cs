using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Reviews.Infrastructure.Entities;

namespace Reviews.Infrastructure;

public class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : DbContext(options)
{
    public const string Schema = "reviews";

    // Column max lengths — exposed so clients (UI / DTO validators) can pick
    // the same numbers from one place. Keeps DB and boundary checks honest.
    public const int SlugMaxLength = 100;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int ImageUrlMaxLength = 500;
    public const int AuthorNameMaxLength = 100;
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 4000;
    public const int MaxImagesPerReview = 5;
    // Per-URL cap on each entry in Review.ImageUrls. Defends against absurdly
    // long URLs (e.g. someone POSTing a base64 data: URI). Our own uploaded
    // paths are ~50 chars; 1000 leaves room for SAS-tokenised blob URLs.
    public const int ReviewImageUrlMaxLength = 1000;

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
            e.Property(p => p.Slug).IsRequired().HasMaxLength(SlugMaxLength);
            e.HasIndex(p => p.Slug).IsUnique();
            e.Property(p => p.Name).IsRequired().HasMaxLength(NameMaxLength);
            e.Property(p => p.Description).IsRequired().HasMaxLength(DescriptionMaxLength);
            e.Property(p => p.ImageUrl).HasMaxLength(ImageUrlMaxLength);
            e.Property(p => p.CreatedAtUtc).HasDefaultValueSql("NOW()");
        });

        b.Entity<Review>(e =>
        {
            e.ToTable("reviews");
            e.HasKey(r => r.Id);
            // Application-side UUIDv7 generation (Sequential.NewGuid) — keep
            // ValueGeneratedNever so EF doesn't try to fill in a Guid.NewGuid()
            // default when callers forget. The seed factory and the controller
            // both supply the value explicitly.
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.AuthorName).IsRequired().HasMaxLength(AuthorNameMaxLength);
            e.Property(r => r.Title).IsRequired().HasMaxLength(TitleMaxLength);
            e.Property(r => r.Body).IsRequired().HasMaxLength(BodyMaxLength);
            e.Property(r => r.ImageUrls).HasColumnType("text[]");

            // Rating is the `Rating` enum on the CLR side, smallint on disk.
            // Member values 1..5 line up with the storage encoding so the
            // existing CHECK on numeric range still applies.
            e.Property(r => r.Rating)
                .HasConversion<short>()
                .HasColumnType("smallint");
            // CHECK constraints reference column names verbatim — quote
            // PascalCase identifiers so Postgres treats them case-sensitively
            // the way EF Core stores them.
            e.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "\"Rating\" BETWEEN 1 AND 5"));

            // Cap how many image URLs a single review can carry. text[] doesn't
            // have a built-in length constraint so it's a CHECK on cardinality.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_reviews_image_count",
                $"array_length(\"ImageUrls\", 1) IS NULL OR array_length(\"ImageUrls\", 1) <= {MaxImagesPerReview}"));

            // Per-URL length cap. Postgres array element constraints aren't
            // a thing, so we evaluate via a subquery over unnest().
            e.ToTable(t => t.HasCheckConstraint(
                "ck_reviews_image_url_length",
                $"(SELECT bool_and(length(u) <= {ReviewImageUrlMaxLength}) FROM unnest(\"ImageUrls\") u) IS NOT FALSE"));

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
            e.Property(r => r.CreatedAtUtc).HasDefaultValueSql("NOW()");
            e.Property(r => r.UpdatedAtUtc).HasDefaultValueSql("NOW()");

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

            // Sort indexes for the three approved-only listings. Each carries
            // the (id) tiebreaker so paging stays deterministic.
            var approvedFilter = $"\"Status\" = {(int)ReviewStatus.Approved}";
            e.HasIndex(r => new { r.ProductId, r.CreatedAtUtc, r.Id })
                .HasDatabaseName("idx_reviews_newest")
                .HasFilter(approvedFilter)
                .IsDescending(false, true, true);
            e.HasIndex(r => new { r.ProductId, r.Score, r.Id })
                .HasDatabaseName("idx_reviews_helpful")
                .HasFilter(approvedFilter)
                .IsDescending(false, true, true);
            e.HasIndex(r => new { r.ProductId, r.Rating, r.CreatedAtUtc, r.Id })
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
            e.Property(v => v.IsUpvote).HasColumnName("IsUpvote");
            e.Property(v => v.CreatedAtUtc).HasDefaultValueSql("NOW()");

            e.HasOne(v => v.Review)
                .WithMany(r => r.Votes)
                .HasForeignKey(v => v.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
