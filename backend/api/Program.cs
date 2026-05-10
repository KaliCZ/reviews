using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Reviews.Api.Auth;
using Reviews.Api.Configuration;
using Reviews.Api.Controllers;
using Reviews.Infrastructure;
using Reviews.Infrastructure.Caching;
using Reviews.Infrastructure.Seeding;
using StrongTypes.EfCore;
using StrongTypes.OpenApi.Swashbuckle;

namespace Reviews.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Per-key files (filename `Section__Sub` → IConfiguration `Section:Sub`).
        // The zitadel-bootstrap container writes its OIDC outputs here at runtime.
        // Lookup chain:
        //   1. API_SECRETS_DIR — Aspire injects this explicitly.
        //   2. REVIEWS_APP_SECRETS_DIR — multi-worktree shared override
        //      (see README, "Sharing infra across worktrees").
        //   3. /run/secrets — docker-compose bind-mount inside the API container.
        //   4. ../../infra/zitadel/.app-secrets — host path for `dotnet watch`
        //      from this project's cwd, single-worktree default.
        // Path.GetFullPath resolves relatives against cwd; absolute paths pass
        // through unchanged.
        var secretsDir =
            Environment.GetEnvironmentVariable("API_SECRETS_DIR")
            ?? Environment.GetEnvironmentVariable("REVIEWS_APP_SECRETS_DIR")
            ?? (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true"
                ? "/run/secrets"
                : "../../infra/zitadel/.app-secrets");
        builder.Configuration.AddKeyPerFile(
            directoryPath: Path.GetFullPath(secretsDir),
            optional: true);

        builder.AddServiceDefaults();

        builder.AddNpgsqlDbContext<ReviewsDbContext>("reviews", configureDbContextOptions: opts =>
            opts.UseNpgsql().UseStrongTypes());

        builder.AddRedisClient(connectionName: "cache");

        builder.AddAzureBlobServiceClient("images");

        builder.Services.AddHttpClient<SeedImageDownloader>();

        var temporalAddress = builder.Configuration.GetRequired<string>("ConnectionStrings:temporal");

        // Lazy: don't open the gRPC connection until first use, so a slow Temporal doesn't crash boot.
        builder.Services.AddTemporalClient(options =>
        {
            options.TargetHost = temporalAddress;
            options.Namespace = "default";
        });

        builder.Services.AddOptions<TurnstileOptions>()
            .Bind(builder.Configuration.GetSection(TurnstileOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddSingleton<IReviewCacheInvalidator, ReviewCacheInvalidator>();

        builder.Services.AddReviewsAuth(builder.Configuration);
        builder.Services.AddReviewsRateLimiting();

        // Default fields include request/response headers — we omit them so the
        // Authorization Bearer token never lands in logs.
        builder.Services.AddHttpLogging(o =>
        {
            o.LoggingFields = HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.RequestQuery
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.Duration;
        });

        builder.Services.AddHealthChecks().AddInfraHealthChecks();

        builder.Services.AddControllers();

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

        // Migrate + seed before accepting traffic. The seeder uses an advisory lock
        // to serialize across replicas. Reviews:AutoApply=false defers to a dedicated
        // migration step in prod.
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

        app.UseHttpLogging();

        app.UseSwagger();
        app.UseSwaggerUI();

        app.UseCors();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapControllers();

        await app.RunAsync();
    }
}
