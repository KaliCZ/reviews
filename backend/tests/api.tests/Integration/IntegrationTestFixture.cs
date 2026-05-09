// Alias to dodge the worker.Program / api.Program collision in the global namespace.
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

// Postgres + Redis + Azurite via Testcontainers, in-process Temporal dev
// server, real API + worker hosts. Single fixture shared across the
// IntegrationTestCollection (container startup amortizes).
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

        // Mirror infra/postgres-init.sh — EF MigrateAsync expects the schema to exist.
        await using (var conn = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS reviews;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        var temporalAddress = temporalEnv.Client.Connection.Options.TargetHost!;

        apiFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:reviews", postgres.GetConnectionString());
            builder.UseSetting("ConnectionStrings:cache", redis.GetConnectionString());
            builder.UseSetting("ConnectionStrings:images", azurite.GetConnectionString());
            builder.UseSetting("ConnectionStrings:temporal", temporalAddress);

            // AddReviewsAuth requires these keys at startup even though
            // TestAuthHandler replaces the runtime scheme.
            builder.UseSetting("Auth:IssuerUrl", "https://test.invalid");
            builder.UseSetting("Auth:Audience", "test-aud");
            builder.UseSetting("Auth:RequireHttps", "false");

            // TurnstileOptions is ValidateOnStart; both keys must be non-empty.
            builder.UseSetting("Turnstile:SiteKey", "test-site-key");
            builder.UseSetting("Turnstile:SecretKey", "test-secret-key");

            builder.UseSetting("Reviews:AutoApply", "true");

            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.Scheme)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                        TestAuthHandler.Scheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                    o.DefaultChallengeScheme = TestAuthHandler.Scheme;
                });

                services.RemoveAll<ITurnstileVerifier>();
                services.AddSingleton<ITurnstileVerifier, AlwaysOkTurnstileVerifier>();
            });
        });

        ApiClient = apiFactory.CreateClient();
        ApiClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(TestAuthHandler.Scheme);

        // Force API startup (migrations + seed) before the worker connects.
        _ = apiFactory.Services;
        await using (var scope = apiFactory.Services.CreateAsyncScope())
        {
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

    // Fresh DbContext for source-of-truth assertions.
    public ReviewsDbContext CreateDbContext()
    {
        var builder = new DbContextOptionsBuilder<ReviewsDbContext>()
            .UseNpgsql(postgres.GetConnectionString());
        builder.UseStrongTypes();
        return new ReviewsDbContext(builder.Options);
    }

    // Bridges "POST 202" → "workflow committed to the DB".
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

    // Mirrors backend/worker/Program.cs with parameterised connection strings.
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
