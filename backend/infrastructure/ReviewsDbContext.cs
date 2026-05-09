using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Reviews.Infrastructure.Entities;

namespace Reviews.Infrastructure;

public class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : DbContext(options)
{
    public const string Schema = "reviews";

    public const int SlugMaxLength = 100;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int ImageUrlMaxLength = 500;
    public const int AuthorNameMaxLength = 100;
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 4000;
    public const int MaxImagesPerReview = 5;
    // 1000 leaves room for SAS-tokenised blob URLs (our own paths are ~50 chars).
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
            // Product IDs come from the upstream catalog; we never generate them.
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
            // We supply UUIDv7 ids ourselves (Sequential.NewGuid); ValueGeneratedNever
            // stops EF from substituting Guid.NewGuid() if a caller forgets.
            e.Property(r => r.Id).ValueGeneratedNever();
            e.Property(r => r.AuthorName).IsRequired().HasMaxLength(AuthorNameMaxLength);
            e.Property(r => r.Title).IsRequired().HasMaxLength(TitleMaxLength);
            e.Property(r => r.Body).IsRequired().HasMaxLength(BodyMaxLength);
            e.Property(r => r.ImageUrls).HasColumnType("text[]");

            e.Property(r => r.Rating)
                .HasConversion<short>()
                .HasColumnType("smallint");
            e.ToTable(t => t.HasCheckConstraint("ck_reviews_rating", "\"Rating\" BETWEEN 1 AND 5"));

            e.ToTable(t => t.HasCheckConstraint(
                "ck_reviews_image_count",
                $"array_length(\"ImageUrls\", 1) IS NULL OR array_length(\"ImageUrls\", 1) <= {MaxImagesPerReview}"));

            // Per-URL length cap is enforced only at the controller (Postgres
            // CHECK can't reach into text[] elements without a DOMAIN type).

            // HasSentinel(Pending): Pending is both the CLR default and the DB
            // default, so EF treats "unset" as "let the DB default fire".
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

            // One live review per (product, author); soft-deleted rows are
            // excluded so a user who deletes their review can re-post.
            e.HasIndex(r => new { r.ProductId, r.AuthorId })
                .IsUnique()
                .HasFilter($"\"Status\" <> {(int)ReviewStatus.Deleted}")
                .HasDatabaseName("uq_reviews_product_author");

            // Id is UUIDv7 so it doubles as a created-at tiebreaker.
            var approvedFilter = $"\"Status\" = {(int)ReviewStatus.Approved}";
            e.HasIndex(r => new { r.ProductId, r.Id })
                .HasDatabaseName("idx_reviews_newest")
                .HasFilter(approvedFilter)
                .IsDescending(false, true);
            e.HasIndex(r => new { r.ProductId, r.Score, r.Id })
                .HasDatabaseName("idx_reviews_helpful")
                .HasFilter(approvedFilter)
                .IsDescending(false, true, true);
            e.HasIndex(r => new { r.ProductId, r.Rating, r.Id })
                .HasDatabaseName("idx_reviews_rating")
                .HasFilter(approvedFilter)
                .IsDescending(false, true, true);
        });

        b.Entity<ReviewVote>(e =>
        {
            e.ToTable("review_votes");
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
