using StrongTypes;

namespace Reviews.Infrastructure.Entities;

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
    public DateTime CreatedAtUtc { get; private set; }

    public ICollection<Review> Reviews { get; private set; } = new List<Review>();
}
