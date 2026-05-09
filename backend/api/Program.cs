using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Reviews.Api.Auth;
using Reviews.Api.Controllers;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Seeding;
using StrongTypes.EfCore;
using StrongTypes.OpenApi.Swashbuckle;

var builder = WebApplication.CreateBuilder(args);

// Per-key file configuration provider — reads every regular file under the
// secrets directory as a single config key. Filenames use the
// `Section__Sub` convention (becomes `Section:Sub` when surfaced to
// IConfiguration). The zitadel-bootstrap container writes its OIDC outputs
// into this directory at runtime; environment variables are loaded by the
// hosting framework on their own and need no custom plumbing.
//
// In docker-compose this is the bind-mounted /run/secrets/. In Aspire (host
// process, not a container) the AppHost passes API_SECRETS_DIR pointing at
// the same host folder.
var secretsDir = Environment.GetEnvironmentVariable("API_SECRETS_DIR") ?? "/run/secrets";
builder.Configuration.AddKeyPerFile(directoryPath: secretsDir, optional: true);

builder.AddServiceDefaults();

// EF Core via Aspire's Npgsql integration. Default migrations history table
// is fine — the DB is isolated per-environment, no cross-app collision risk
// to design around.
builder.AddNpgsqlDbContext<ReviewsDbContext>("reviews", configureDbContextOptions: opts =>
    opts.UseNpgsql().UseStrongTypes());

builder.AddRedisClient(connectionName: "cache");

// Blob storage for review images. Aspire registers a BlobServiceClient using
// the "images" connection (Azurite locally, real Azure Blob in prod).
builder.AddAzureBlobServiceClient("images");

// Typed HttpClient for the seed-time picsum downloader. Registered against the
// concrete Seeder type so the seeder gets its own client instance with its
// own resilience / pooling defaults.
builder.Services.AddHttpClient<SeedImageDownloader>();

var temporalAddress = builder.Configuration.GetConnectionString("temporal")
    ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

// Lazy Temporal client — doesn't open the gRPC connection until first use,
// so a slow-to-start Temporal at boot doesn't crash the API.
builder.Services.AddTemporalClient(options =>
{
    options.TargetHost = temporalAddress;
    options.Namespace = "default";
});

// Bind + validate the Turnstile section at startup. ValidateOnStart turns
// "missing key" into a boot-time crash instead of a request-time silent
// failure.
builder.Services.AddOptions<TurnstileOptions>()
    .Bind(builder.Configuration.GetSection(TurnstileOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Auth (ZITADEL JwtBearer), CurrentUser, Turnstile.
builder.Services.AddReviewsAuth(builder.Configuration);
// Per-user + per-IP rate limiting, applied to the write controller.
builder.Services.AddReviewsRateLimiting();

builder.Services.AddHealthChecks().AddInfraHealthChecks();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
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
    options.SupportNonNullableReferenceTypes();
    options.NonNullableReferenceTypesAsRequired();
    options.AddStrongTypes();
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

// Migrate + seed before accepting traffic. EF Core's MigrateAsync is
// idempotent and concurrency-safe at the schema-application level; the seed
// runner uses an advisory lock for the cross-replica path. Configurable via
// Reviews:AutoApply for production deployments where a dedicated migration
// step is preferred.
if (app.Configuration.GetValue("Reviews:AutoApply", true))
{
    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        await db.Database.MigrateAsync();
    }
    await Seeder.RunAsync(app.Services);
}

app.MapDefaultEndpoints();

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

app.Run();

public partial class Program { }
