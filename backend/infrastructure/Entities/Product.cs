namespace Reviews.Infrastructure.Entities;

// Catalog product. ID is provided by the upstream catalog (this service doesn't
// generate them — see ReviewsDbContext.OnModelCreating where Id is configured
// ValueGeneratedNever()). Slug is a URL-safe identifier and must be unique.
public class Product
{
    private Product() { }

    public Product(long id, string slug, string name, string description, string? imageUrl = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Id = id;
        Slug = slug;
        Name = name;
        Description = description;
        ImageUrl = imageUrl;
    }

    public long Id { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? ImageUrl { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public ICollection<Review> Reviews { get; private set; } = new List<Review>();
}
