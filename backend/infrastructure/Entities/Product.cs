namespace Reviews.Infrastructure.Entities;

public class Product
{
    public long Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<Review> Reviews { get; set; } = new List<Review>();
}
