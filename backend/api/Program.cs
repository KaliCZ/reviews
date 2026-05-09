using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Reviews.Api.Auth;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;
using StrongTypes.EfCore;
using StrongTypes.OpenApi.Swashbuckle;

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
    opts.UseStrongTypes();
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

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Enums on the wire as their string names — generated TS clients
        // get readable union literals (`'newest' | 'helpful' | …`) instead
        // of bare integers.
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());

        // Treat C#'s nullable annotations as enforceable on the wire: a JSON
        // payload that omits a non-nullable property (or sends `null` for
        // it) fails deserialization with JsonException instead of silently
        // binding null into a NonEmptyString slot. ASP.NET turns the
        // exception into a 400 before the action runs.
        options.JsonSerializerOptions.RespectNullableAnnotations = true;
        options.JsonSerializerOptions.RespectRequiredConstructorParameters = true;
    });

// Swashbuckle's spec generator + UI. Picked over Microsoft.AspNetCore.OpenApi
// because the StrongTypes integration is cleaner on this pipeline (the skill's
// openapi.md spells out the rough edges in the Microsoft hooks).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Reviews API", Version = "v1" });
    // Surface C#'s nullability into the spec — `string` is required, `string?`
    // is optional, ditto for the StrongTypes wrappers. Without this,
    // Swashbuckle marks every property as optional and the generated TS
    // client lets every field be undefined.
    options.SupportNonNullableReferenceTypes();
    options.AddStrongTypes();
    options.SchemaFilter<RequireNonNullableSchemaFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "ZITADEL access token (Bearer).",
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});

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

// Spec is always exposed (the SPA's client-codegen step pulls it from a running
// dev API); Swagger UI is dev-only since prod traffic shouldn't browse it.
app.UseSwagger();
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program { }
