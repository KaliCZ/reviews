// Worker project's top-level Program collides with the API's at the global
// namespace. Alias the worker assembly so we can reach Reviews.Worker.* types
// without dragging its Program into the picture.
extern alias worker;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Reviews.Api.Services;
using Reviews.Infrastructure;
using Reviews.Shared;
using StrongTypes.EfCore;
using Temporalio.Client;
using Temporalio.Extensions.Hosting;
using Temporalio.Testing;
using Testcontainers.Azurite;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using ReviewActivities = worker::Reviews.Worker.ReviewActivities;

namespace Reviews.Api.Tests.Integration;

// Real-everything integration fixture. Spins up Postgres + Redis + Azurite via
// Testcontainers, an in-process Temporal dev server via WorkflowEnvironment,
// then boots the API (WebApplicationFactory) and a worker host pointed at the
// same containers. Tests drive HTTP against ApiClient, send Temporal signals
// via TemporalClient, and verify side effects via fresh DbContexts.
//
// Lifetime: a single instance is shared across all tests in the
// IntegrationTestCollection — container startup is the expensive part, so
// amortise it. Tests read seeded products and add their own reviews; the
// TestAuthHandler issues a synthetic sub that hashes to a Guid which doesn't
// collide with any seeded AuthorId (see SeedDefinitions.Alice etc.), so the
// test user starts from a clean slate on every product.
public sealed class IntegrationTestFixture : IAsyncLifetime
{
    private PostgreSqlContainer postgres = null!;
    private RedisContainer redis = null!;
    private AzuriteContainer azurite = null!;
    private WorkflowEnvironment temporalEnv = null!;
    private WebApplicationFactory<Program> apiFactory = null!;
    private IHost workerHost = null!;

    public HttpClient ApiClient { get; private set; } = null!;
    public ITemporalClient TemporalClient => temporalEnv.Client;

    public async ValueTask InitializeAsync()
    {
        postgres = new PostgreSqlBuilder()
            .WithImage("postgres:17-alpine")
            .WithDatabase("reviews")
            .WithUsername("reviews")
            .WithPassword("reviews")
            .Build();
        redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        azurite = new AzuriteBuilder()
            .WithImage("mcr.microsoft.com/azure-storage/azurite:latest")
            .Build();

        await Task.WhenAll(
            postgres.StartAsync(),
            redis.StartAsync(),
            azurite.StartAsync(),
            StartTemporalAsync());

        // Pre-create the `reviews` schema. Production does this via
        // infra/postgres-init.sh; in tests we run the equivalent before EF
        // Core's MigrateAsync (whose history table lives outside the schema
        // but whose CREATE TABLE statements target it).
        await using (var conn = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS reviews;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var temporalAddress = temporalEnv.Client.Connection.Options.TargetHost!;

        apiFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Aspire's typed component registrations read these connection
            // strings from configuration. WebApplicationFactory's UseSetting
            // lands them at the top of the IConfiguration chain.
            builder.UseSetting("ConnectionStrings:reviews", postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:cache", redis.GetConnectionString());
            builder.UseSetting("ConnectionStrings:images", azurite.GetConnectionString());
            builder.UseSetting("ConnectionStrings:temporal", temporalAddress);

            // Auth stubs — the real JwtBearer scheme is replaced below by the
            // TestAuthHandler, but AddReviewsAuth still asserts these keys
            // exist at startup before the handler ever runs.
            builder.UseSetting("Auth:IssuerUrl", "https://test.invalid");
            builder.UseSetting("Auth:Audience", "test-aud");
            builder.UseSetting("Auth:RequireHttps", "false");

            // TurnstileOptions is ValidateOnStart — needs both keys non-empty.
            // SiteKey is read by /api/config; SecretKey is used by the real
            // verifier (which we replace with AlwaysOkTurnstileVerifier below).
            builder.UseSetting("Turnstile:SiteKey", "test-site-key");
            builder.UseSetting("Turnstile:SecretKey", "test-secret-key");

            // Run migrations + seed on boot. This is the prod default; we keep
            // it on for tests so the integration suite exercises the same
            // startup path.
            builder.UseSetting("Reviews:AutoApply", "true");

            builder.ConfigureServices(services =>
            {
                // Replace JwtBearer with the always-authenticates TestAuthHandler.
                services.AddAuthentication(TestAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.Scheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                });

                // Stub out the real Cloudflare verifier — tests post a stub
                // token and the verifier returns true unconditionally.
                services.RemoveAll<ITurnstileVerifier>();
                services.AddSingleton<ITurnstileVerifier, AlwaysOkTurnstileVerifier>();
            });
        });

        ApiClient = apiFactory.CreateClient();
        ApiClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestAuthHandler.Scheme);

        // Force the API host to start (and run migrations + seed) before the
        // worker host comes up so the worker's first connect doesn't race the
        // schema creation.
        _ = apiFactory.Services;
        await using (var scope = apiFactory.Services.CreateAsyncScope())
        {
            // Touch the DbContext to force lazy initialisation paths to settle.
            var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT 1");
        }

        workerHost = BuildWorkerHost(
            postgres.GetConnectionString(),
            redis.GetConnectionString(),
            temporalAddress);
        await workerHost.StartAsync();
    }

    private async Task StartTemporalAsync()
    {
        // Downloads + runs the Temporal CLI dev server in-process. Faster than
        // the auto-setup container and a single binary instead of
        // Temporal+Postgres+Cassandra. Default namespace is "default", which
        // matches the API/worker config.
        temporalEnv = await WorkflowEnvironment.StartLocalAsync();
    }

    public async ValueTask DisposeAsync()
    {
        try { if (workerHost is not null) await workerHost.StopAsync(TimeSpan.FromSeconds(5)); } catch { }
        try { workerHost?.Dispose(); } catch { }
        try { if (apiFactory is not null) await apiFactory.DisposeAsync(); } catch { }
        try { if (temporalEnv is not null) await temporalEnv.ShutdownAsync(); } catch { }
        try { if (azurite is not null) await azurite.DisposeAsync(); } catch { }
        try { if (redis is not null) await redis.DisposeAsync(); } catch { }
        try { if (postgres is not null) await postgres.DisposeAsync(); } catch { }
    }

    // Fresh DbContext for assertions — the API's tracked context lives one
    // request away, so tests use this to query the source of truth without
    // pulling on the request-scoped instance.
    public ReviewsDbContext CreateDbContext()
    {
        // UseStrongTypes returns the non-generic builder, so chain it on a
        // separate line to keep .Options on the typed builder.
        var builder = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseNpgsql(postgres.GetConnectionString());
        builder.UseStrongTypes();
        return new ReviewsDbContext(builder.Options);
    }

    // Polls `probe` until it returns a non-null value or the deadline elapses.
    // Used to bridge "POST returns 202" → "the workflow finished writing to
    // the DB". Workflows complete in milliseconds with the local dev server,
    // but the API call returns before the activity has even started, so a
    // tiny poll loop is the cleanest synchronisation point.
    public async Task<T> WaitForAsync<T>(
        Func<Task<T?>> probe,
        TimeSpan? timeout = null,
        string? what = null) where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null) return result;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Probe '{what ?? "<unnamed>"}' did not return a value within the timeout.");
    }

    // Builds a self-contained worker host mirroring backend/worker/Program.cs
    // but parameterised on connection strings (instead of pulling them from
    // Aspire's host). Registers the same workflows + activities so the test
    // hits the real worker code path.
    private static IHost BuildWorkerHost(
        string postgresConnectionString,
        string redisConnectionString,
        string temporalAddress)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:reviews"] = postgresConnectionString,
            ["ConnectionStrings:cache"] = redisConnectionString,
            ["ConnectionStrings:temporal"] = temporalAddress,
        });

        builder.AddNpgsqlDbContext<ReviewsDbContext>("reviews", configureDbContextOptions: opts =>
            opts.UseNpgsql().UseStrongTypes());
        builder.AddRedisClient(connectionName: "cache");

        builder.Services
            .AddTemporalClient(options =>
            {
                options.TargetHost = temporalAddress;
                options.Namespace = "default";
            })
            .AddHostedTemporalWorker(taskQueue: ReviewQueues.TaskQueue)
            .AddWorkflow<SubmitReviewWorkflow>()
            .AddWorkflow<EditReviewWorkflow>()
            .AddWorkflow<DeleteReviewWorkflow>()
            .AddWorkflow<RateReviewWorkflow>()
            .AddScopedActivities<ReviewActivities>();

        return builder.Build();
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
    public const string Name = "integration";
}
