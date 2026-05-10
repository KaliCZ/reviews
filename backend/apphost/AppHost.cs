using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var zitadelMasterkey = builder.AddParameter("zitadel-masterkey", secret: true);

// Per-worktree namespace key, used to scope shared state (secrets dirs,
// postgres volume, container groups) so parallel AppHosts don't collide.
var worktreeId = Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes(Path.GetFullPath(Environment.CurrentDirectory))))[..8].ToLowerInvariant();

// Per-worktree offset for the few services pinned below. Mirrored in
// scripts/aspire.mjs for the dashboard listeners.
var portOffset = (int)(uint.Parse(worktreeId[..4], System.Globalization.NumberStyles.HexNumber) % 1000);

// Pinned services: OIDC redirect target, gRPC (no auto-allocate), and
// browser-facing UIs where stable bookmarks help. Everything else gets a
// random Aspire-allocated port. Bases spaced 1000 apart; 24000-27999 is
// reserved for the AppHost's own listeners (scripts/aspire.mjs).
var temporalGrpcPort  = 17000 + portOffset;
var temporalUiPort    = 18000 + portOffset;
var webPort           = 19000 + portOffset;
var pgAdminPort       = 22000 + portOffset;
var redisInsightPort  = 23000 + portOffset;
var zitadelPort       = 31000 + portOffset;

// Stamps every container with com.docker.compose.project so Docker Desktop
// shows them as one collapsible group per AppHost.
var dockerGroup = $"reviews-aspire-{worktreeId}";

// One per-AppHost postgres hosts all four logical DBs; isolation between
// parallel AppHosts comes from the worktree-scoped data volume.
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume($"reviews-aspire-postgres-{worktreeId}")
    .WithBindMount("../../infra/postgres-init.sh", "/docker-entrypoint-initdb.d/postgres-init.sh", isReadOnly: true)
    .WithEnvironment("POSTGRES_MULTIPLE_DATABASES", "reviews,zitadel,temporal,temporal_visibility")
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(pgAdminPort).WithDockerGroup(dockerGroup))
    .WithDockerGroup(dockerGroup);

var reviewsDb = postgres.AddDatabase("reviews");
var zitadelDb = postgres.AddDatabase("zitadel-db", databaseName: "zitadel");
var temporalDb = postgres.AddDatabase("temporal-db", databaseName: "temporal");
var temporalVisibilityDb = postgres.AddDatabase("temporal-visibility-db", databaseName: "temporal_visibility");

var cache = builder.AddRedis("cache")
    .WithRedisInsight(insight => insight
        .WithHostPort(redisInsightPort)
        .WithHttpHealthCheck("/api/health")
        .WithDockerGroup(dockerGroup))
    .WithDockerGroup(dockerGroup);

// rediss:// URL for the JS BFF (the .NET integrations get the connection
// string directly via WithReference, but the BFF needs an env var).
var redisUrl = ReferenceExpression.Create(
    $"rediss://default:{cache.Resource.PasswordParameter!}@{cache.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{cache.GetEndpoint("tcp").Property(EndpointProperty.Port)}");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDockerGroup(dockerGroup));
var images = storage.AddBlobs("images");

// Per-worktree secrets dirs (overridable via REVIEWS_*_SECRETS_DIR).
// Survive AppHost restarts so bootstrap.sh's smart-skip can short-circuit.
var sharedRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".reviews-dev",
    "aspire",
    worktreeId);
var zitadelSecrets = Environment.GetEnvironmentVariable("REVIEWS_ZITADEL_SECRETS_DIR")
    ?? Path.Combine(sharedRoot, "zitadel-secrets");
var appSecrets = Environment.GetEnvironmentVariable("REVIEWS_APP_SECRETS_DIR")
    ?? Path.Combine(sharedRoot, "app-secrets");
Directory.CreateDirectory(zitadelSecrets);
Directory.CreateDirectory(appSecrets);

// Web is declared early so zitadel-bootstrap can register its OIDC redirect
// URI; full config is chained at the bottom once api exists.
// isProxied:false avoids Aspire's two-port proxy/inner split — the BFF and
// the browser would otherwise see different ports and the OIDC redirect
// would mismatch. The trade-off is that an unproxied endpoint requires a
// pinned port. PORT is forwarded into ng serve by web/scripts/serve.mjs.
var web = builder.AddJavaScriptApp("web", "../../web", "start")
    .WithHttpEndpoint(port: webPort, env: "PORT", isProxied: false)
    .WithExternalHttpEndpoints()
    // Probes SSR, not the node process — Healthy should mean the page renders.
    .WithHttpHealthCheck("/");
// Plain strings, not ReferenceExpression: a Property() reference would add
// an implicit bootstrap→web dependency and deadlock with web→bootstrap.
var bffRedirectUri = $"http://localhost:{webPort}/auth/callback";
var bffPostLogoutUri = $"http://localhost:{webPort}/";

// Pinned to v2.71.2: v4 defaults to LoginV2, a separate Next.js app not
// bundled in the image, so the embedded /ui/login redirect breaks.
var zitadel = builder.AddContainer("zitadel", "ghcr.io/zitadel/zitadel", "v2.71.2")
    .WithArgs("start-from-init", "--masterkeyFromEnv", "--steps", "/steps.yaml")
    .WithBindMount("../../infra/zitadel/steps.yaml", "/steps.yaml", isReadOnly: true)
    .WithBindMount(zitadelSecrets, "/zitadel-secrets")
    .WithEnvironment("ZITADEL_MASTERKEY", zitadelMasterkey)
    .WithEnvironment("ZITADEL_EXTERNALSECURE", "false")
    .WithEnvironment("ZITADEL_EXTERNALDOMAIN", "localhost")
    .WithEnvironment("ZITADEL_TLS_ENABLED", "false")
    .WithEnvironment("ZITADEL_DEFAULTINSTANCE_FEATURES_LOGINV2_REQUIRED", "false")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_HOST", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.TargetPort))
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_DATABASE", "zitadel")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_USERNAME", "postgres")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_PASSWORD", postgresPassword)
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_SSL_MODE", "disable")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_USERNAME", "postgres")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_PASSWORD", postgresPassword)
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_SSL_MODE", "disable")
    .WithHttpEndpoint(name: "console", port: zitadelPort, targetPort: 8080)
    // Gate for WaitFor(zitadel) below: flips Healthy once FirstInstance
    // completes and the management API is reachable.
    .WithHttpHealthCheck("/debug/ready", endpointName: "console")
    .WithDockerGroup(dockerGroup)
    .WaitFor(zitadelDb);

// EXTERNALPORT must match the published host port so the issuer URL in
// JWTs and the OIDC redirects line up with what the browser sees.
var zitadelEndpoint = zitadel.GetEndpoint("console");
zitadel.WithEnvironment("ZITADEL_EXTERNALPORT", zitadelEndpoint.Property(EndpointProperty.Port));
var zitadelPublicUrl = ReferenceExpression.Create(
    $"http://localhost:{zitadelEndpoint.Property(EndpointProperty.Port)}");

// One-shot: provisions the OIDC app + test user, writes client secrets to
// appSecrets for api and web. INTERNAL_URL is the docker-network sibling;
// PUBLIC_URL is what the browser sees (used for issuer + redirects).
var zitadelBootstrap = builder.AddContainer("zitadel-bootstrap", "curlimages/curl", "8.10.1")
    .WithEntrypoint("/bin/sh")
    .WithArgs("-c", "/bootstrap.sh")
    .WithBindMount("../../infra/zitadel/bootstrap.sh", "/bootstrap.sh", isReadOnly: true)
    .WithBindMount(zitadelSecrets, "/zitadel-secrets", isReadOnly: true)
    .WithBindMount(appSecrets, "/app-secrets")
    .WithEnvironment("ZITADEL_INTERNAL_URL", "http://zitadel:8080")
    .WithEnvironment("ZITADEL_PUBLIC_URL", zitadelPublicUrl)
    .WithEnvironment("BFF_REDIRECT_URIS", bffRedirectUri)
    .WithEnvironment("BFF_POST_LOGOUT_URIS", bffPostLogoutUri)
    // Used by bootstrap.sh to print real paths in its recovery message.
    .WithEnvironment("WORKTREE_ID", worktreeId)
    .WithDockerGroup(dockerGroup)
    .WaitFor(zitadel);

// Temporal is gRPC-only, so we probe the port directly. Auto-setup opens
// it last, so port-open ≈ ready in practice.
builder.Services.AddHealthChecks().AddAsyncCheck("temporal-grpc-tcp", async () =>
{
    try
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await client.ConnectAsync("localhost", temporalGrpcPort, cts.Token);
        return HealthCheckResult.Healthy();
    }
    catch (Exception ex)
    {
        return HealthCheckResult.Unhealthy(ex.Message);
    }
});

// scheme:"tcp" doesn't auto-allocate, and the canonical 7233 would collide
// across worktrees and with compose.
var temporal = builder.AddContainer("temporal", "temporalio/auto-setup", "latest")
    .WithEndpoint(name: "grpc", port: temporalGrpcPort, targetPort: 7233, scheme: "tcp")
    .WithEnvironment("DB", "postgres12")
    .WithEnvironment("DB_PORT", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.TargetPort))
    .WithEnvironment("POSTGRES_USER", "postgres")
    .WithEnvironment("POSTGRES_PWD", postgresPassword)
    .WithEnvironment("POSTGRES_SEEDS", postgres.Resource.PrimaryEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("DBNAME", "temporal")
    .WithEnvironment("VISIBILITY_DBNAME", "temporal_visibility")
    .WithEnvironment("ENABLE_ES", "false")
    .WithHealthCheck("temporal-grpc-tcp")
    .WithDockerGroup(dockerGroup)
    .WaitFor(temporalDb)
    .WaitFor(temporalVisibilityDb);

var temporalUi = builder.AddContainer("temporal-ui", "temporalio/ui", "latest")
    .WithHttpEndpoint(port: temporalUiPort, targetPort: 8080)
    .WithHttpHealthCheck("/")
    .WithEnvironment("TEMPORAL_ADDRESS", ReferenceExpression.Create(
        $"{temporal.GetEndpoint("grpc").Property(EndpointProperty.Host)}:{temporal.GetEndpoint("grpc").Property(EndpointProperty.TargetPort)}"))
    .WithDockerGroup(dockerGroup)
    .WaitFor(temporal);

// temporal-ui's dashboard JS XHRs back to its own origin, so CORS has to
// match the assigned host port.
var temporalUiEndpoint = temporalUi.GetEndpoint("http");
temporalUi.WithEnvironment("TEMPORAL_CORS_ORIGINS", ReferenceExpression.Create(
    $"http://localhost:{temporalUiEndpoint.Property(EndpointProperty.Port)}"));

// api / worker run on the host, so they need the published host port
// (Port, not TargetPort which is the container-internal 7233).
var temporalConnString = ReferenceExpression.Create(
    $"{temporal.GetEndpoint("grpc").Property(EndpointProperty.Host)}:{temporal.GetEndpoint("grpc").Property(EndpointProperty.Port)}");

// launchProfileName:null so AppHost doesn't grab launchSettings' :5146 —
// that port belongs to `npm run dev`'s `dotnet watch`. API owns migrations
// + seed; worker waits on API health before querying.
var api = builder.AddProject<Projects.api>("api", launchProfileName: null)
    .WithHttpEndpoint()
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(images).WaitFor(storage)
    .WithEnvironment("REVIEWS_APP_SECRETS_DIR", appSecrets)
    .WithEnvironment("ConnectionStrings__temporal", temporalConnString)
    // Overrides the appsettings :8080 fallback; under Aspire zitadel is on
    // a random host port and JwtBearer would otherwise hammer 8080.
    .WithEnvironment("Auth__IssuerUrl", zitadelPublicUrl)
    .WaitFor(temporal)
    .WaitForCompletion(zitadelBootstrap);

// WithHttpEndpoint() pulls the port off ASP.NET's default :5000, which
// parallel AppHosts would otherwise fight over.
var workerService = builder.AddProject<Projects.worker>("worker")
    .WithHttpEndpoint()
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(images).WaitFor(storage)
    .WithEnvironment("ConnectionStrings__temporal", temporalConnString)
    .WaitFor(temporal)
    .WaitFor(api);

// Web's full config (endpoint was declared at the top for bootstrap's sake).
web
    .WithReference(api).WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEnvironment("REVIEWS_APP_SECRETS_DIR", appSecrets)
    // Both collapse to the same URL in Aspire (web runs on the host); compose
    // differs because that route crosses container boundaries.
    .WithEnvironment("ZITADEL_PUBLIC_URL", zitadelPublicUrl)
    .WithEnvironment("ZITADEL_INTERNAL_URL", zitadelPublicUrl)
    .WithEnvironment("REDIS_URL", redisUrl)
    .WithEnvironment("REDIS_TLS_INSECURE", "true")
    .WithEnvironment("SESSION_SECRET", "dev-only-session-secret-rotate-in-prod")
    .WithOtlpExporter()
    .WaitForCompletion(zitadelBootstrap);

api.WithEnvironment("WEB_ORIGIN", web.GetEndpoint("http"));

builder.Build().Run();
