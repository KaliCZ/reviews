using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using StrongTypes.EfCore;

namespace Reviews.Infrastructure;

// Used only by `dotnet ef migrations …` from this project. The runtime path
// gets its DbContext from Aspire (`builder.AddNpgsqlDbContext<…>("reviews")`),
// which builds the connection string from configuration. Design-time has no
// configuration, so we hard-code the local-dev connection here. Anyone running
// migrations against another environment overrides via REVIEWS_DB env var.
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("REVIEWS_DB")
            ?? "Host=localhost;Database=reviews;Username=postgres;Password=postgres;Search Path=reviews";

        var builder = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseNpgsql(conn, o => o.MigrationsHistoryTable("__ef_migrations_history", ReviewsDbContext.Schema));
        builder.UseStrongTypes();
        return new ReviewsDbContext(builder.Options);
    }
}
