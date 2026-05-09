using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using StrongTypes.EfCore;

namespace Reviews.Infrastructure;

// `dotnet ef migrations …` only. Override the local-dev connection via REVIEWS_DB.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("REVIEWS_DB")
            ?? "Host=localhost;Database=reviews;Username=postgres;Password=postgres;Search Path=reviews";

        var builder = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseNpgsql(conn);
        builder.UseStrongTypes();
        return new ReviewsDbContext(builder.Options);
    }
}
