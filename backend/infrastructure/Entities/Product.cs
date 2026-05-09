using StrongTypes;

namespace Reviews.Infrastructure.Entities;

// Catalog product. ID is provided by the upstream catalog (this service doesn't
// generate them — see ReviewsDbContext.OnModelCreating where Id is configured
// ValueGeneratedNever()). Slug is a URL-safe identifier and must be unique.
public class Product
{
    private Product() { }

    public Product(long id, NonEmptyString slug, NonEmptyString name, NonEmptyString description, NonEmptyString? imageUrl = null)
    {
        Id = id;
        Slug = slug;
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
    }

    public long Id { get; private set; }
    public NonEmptyString Slug { get; private set; } = null!;
    public NonEmptyString Name { get; private set; } = null!;
    public NonEmptyString Description { get; private set; } = null!;
    public NonEmptyString? ImageUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public ICollection<Review> Reviews { get; private set; } = new List<Review>();
}
