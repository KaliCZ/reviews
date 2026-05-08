using Microsoft.EntityFrameworkCore;
using Reviews.Api.Auth;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;

// zitadel-bootstrap writes ZITADEL_ISSUER / ZITADEL_CLIENT_ID / ZITADEL_CLIENT_SECRET
// into /run/secrets/zitadel.env (compose) or the path at $ZITADEL_ENV_FILE
// (Aspire) at runtime. Pull them into process env before building config.
DotEnvLoader.LoadDefaults();

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// EF Core via Aspire's Npgsql integration — same connection-name plumbing
// (`reviews`) as the raw Npgsql client; this just adds a typed DbContext
// alongside. Health checks, OTel, and resilience come along for free.
builder.AddNpgsqlDbContext<ReviewsDbContext>("reviews", configureDbContextOptions: opts =>
{
    opts.UseNpgsql(o => o.MigrationsHistoryTable("__ef_migrations_history", ReviewsDbContext.Schema));
});

builder.AddRedisClient(connectionName: "cache");

// Blob storage for review images. Aspire registers a BlobServiceClient using
// the "images" connection (Azurite locally, real Azure Blob in prod).
builder.AddAzureBlobServiceClient("images");

// HttpClientFactory for the seed-time picsum downloader. Named so the seed
// path can ask for it specifically; defaults are fine.
builder.Services.AddHttpClient("seed-images");

var temporalAddress = builder.Configuration.GetConnectionString("temporal")
    ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

// Lazy Temporal client — doesn't open the gRPC connection until first use,
// so a slow-to-start Temporal at boot doesn't crash the API.
builder.Services.AddTemporalClient(options =>
{
    options.TargetHost = temporalAddress;
    options.Namespace = "default";
});

// Auth (ZITADEL JwtBearer), CurrentUser, Turnstile.
builder.Services.AddReviewsAuth(builder.Configuration, builder.Environment);
// Rate limiting on the write policy — IP + user_id partition, fixed window.
builder.Services.AddReviewsRateLimiting();

builder.Services.AddHealthChecks().AddInfraHealthChecks();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration["WEB_ORIGIN"] ?? "http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()); // BFF forwards Bearer tokens; cookies stay at the BFF
});

var app = builder.Build();

// Migrate + seed before accepting traffic. Both are advisory-lock-protected
// so it's safe to run from multiple API replicas — the first wins, the rest
// see "no pending migrations / products already seeded" and continue.
// Configurable via Reviews:AutoApply for production deployments where a
// dedicated migration step is preferred.
if (app.Configuration.GetValue("Reviews:AutoApply", true))
{
    await MigrationRunner.ApplyAsync(app.Services);
    await Seeder.RunAsync(app.Services);
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program { }
