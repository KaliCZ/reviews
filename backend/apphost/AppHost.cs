using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var zitadelMasterkey = builder.AddParameter("zitadel-masterkey", secret: true);

// Per-worktree namespace key derived from the AppHost's working directory
// (each git worktree has a unique repo path). Used to scope per-worktree
// state — secrets folders and the postgres data volume — so two parallel
// `npm run aspire` runs in different worktrees don't fight over the same
// host paths or named volumes. Within a worktree, the same id keeps state
// stable across AppHost restarts; across worktrees, state is fully isolated.
var worktreeId = Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes(Path.GetFullPath(Environment.CurrentDirectory))))[..8].ToLowerInvariant();

// Single per-AppHost postgres serves the app, ZITADEL, and Temporal — no
// dedicated singleton for ZITADEL because we'd then need to coordinate two
// stateful singletons across parallel AppHosts (and worry about the bootstrap
// container being on a different docker network than the singleton). Aspire
// plays best with everything per-session; isolation comes from the
// worktree-scoped data volume.
var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume($"reviews-aspire-postgres-{worktreeId}")
    .WithBindMount("../../infra/postgres-init.sh", "/docker-entrypoint-initdb.d/postgres-init.sh", isReadOnly: true)
    .WithEnvironment("POSTGRES_MULTIPLE_DATABASES", "reviews,zitadel,temporal,temporal_visibility")
    .WithPgAdmin();

var reviewsDb = postgres.AddDatabase("reviews");
var zitadelDb = postgres.AddDatabase("zitadel-db", databaseName: "zitadel");
var temporalDb = postgres.AddDatabase("temporal-db", databaseName: "temporal");
var temporalVisibilityDb = postgres.AddDatabase("temporal-visibility-db", databaseName: "temporal_visibility");

var cache = builder.AddRedis("cache")
    .WithRedisInsight();

// Aspire 13.3 generates a Redis password and exposes the primary endpoint over
// TLS with a self-signed dev cert. The .NET integrations consume the resource's
// connection string directly; the JS BFF needs a redis-URL-shaped env var, so
// build one here. REDIS_TLS_INSECURE tells the BFF to skip cert validation —
// prod uses CA-signed certs and leaves it unset.
var redisUrl = ReferenceExpression.Create(
    $"rediss://default:{cache.Resource.PasswordParameter!}@{cache.GetEndpoint("tcp").Property(EndpointProperty.Host)}:{cache.GetEndpoint("tcp").Property(EndpointProperty.Port)}");

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var images = storage.AddBlobs("images");

// Per-worktree secrets dirs under ~/.reviews-dev/aspire/<worktree-id>/. The
// bootstrap PAT and OIDC client secret persist across AppHost restarts within
// a worktree (so the smart-skip in bootstrap.sh can short-circuit), but each
// worktree is fully isolated from every other worktree and from compose's
// `~/.reviews-dev/`. Override with REVIEWS_*_SECRETS_DIR if you need a custom
// location.
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

// State pairing: ZITADEL writes admin-pat.txt during FirstInstance and
// never rewrites it; the DB volume remembers FirstInstance has run. Wipe
// either half (manually, or by switching docker contexts) and bootstrap
// deadlocks waiting for a PAT that won't reappear. Detect the mismatch
// and reset both halves so the next boot is a clean re-bootstrap. Cheap
// safety net — under normal use both halves move together.
var postgresVolumeName = $"reviews-aspire-postgres-{worktreeId}";
var hasPat = File.Exists(Path.Combine(zitadelSecrets, "admin-pat.txt"));
var hasVolume = DockerVolumeExists(postgresVolumeName);
if (hasPat != hasVolume)
{
    Console.WriteLine($"[apphost] secrets/volume out of sync (pat={hasPat}, volume={hasVolume}) — wiping both for a clean re-bootstrap");
    Directory.Delete(zitadelSecrets, recursive: true);
    Directory.Delete(appSecrets, recursive: true);
    Directory.CreateDirectory(zitadelSecrets);
    Directory.CreateDirectory(appSecrets);
    if (hasVolume) RunDocker("volume", "rm", postgresVolumeName);
}

static bool DockerVolumeExists(string name)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("volume");
        psi.ArgumentList.Add("inspect");
        psi.ArgumentList.Add(name);
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return false;
        p.WaitForExit(5000);
        return p.ExitCode == 0;
    }
    catch { return false; }
}

static void RunDocker(params string[] args)
{
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi);
        p?.WaitForExit(10000);
    }
    catch { /* best-effort cleanup — Aspire will surface the real error if it matters */ }
}

// Web is declared up front (just the endpoint) so zitadel-bootstrap below can
// inject the OIDC redirect URI for whatever host port Aspire ends up
// assigning. Aspire's PORT env var is forwarded into `ng serve --port`
// by web/scripts/serve.mjs (Angular's dev server otherwise ignores PORT
// and binds to its 4200 default), so pinning here actually moves ng's
// listener too — parallel worktrees end up on different ports.
//
// isProxied: false collapses Aspire's normal two-port setup (proxy port +
// inner port) into one. Without it, the BFF reads PORT (the inner port)
// and tells ZITADEL to redirect there, while the browser is on the proxy
// port and bootstrap registered the proxy port — mismatch. Web only has
// one caller (the browser), so the proxy hop adds no value here. The
// trade-off is that isProxied: false requires an explicit port (Aspire
// won't auto-allocate one for unproxied endpoints), so we derive a
// stable per-worktree port the same way temporal does below.
var webPort = 19000 + (int)(uint.Parse(worktreeId[..4], System.Globalization.NumberStyles.HexNumber) % 1000);
var web = builder.AddJavaScriptApp("web", "../../web", "start")
    .WithHttpEndpoint(port: webPort, env: "PORT", isProxied: false)
    .WithExternalHttpEndpoints();
// Plain-string URLs — NOT ReferenceExpression — because using
// webEndpoint.Property(...) here would make bootstrap implicitly wait for
// web's endpoint, while web already does WaitForCompletion(zitadelBootstrap)
// → deadlock. Since webPort is a known int at C# evaluation time we can
// interpolate it directly and skip the implicit dependency.
var bffRedirectUri = $"http://localhost:{webPort}/auth/callback";
var bffPostLogoutUri = $"http://localhost:{webPort}/";

// Per-AppHost ZITADEL: each AppHost gets its own container + its own DB
// (under the per-AppHost postgres above). Random host port lets parallel
// AppHosts coexist; ZITADEL_PUBLIC_URL is computed from the assigned port
// and pumped into bootstrap + web so the OIDC issuer URL stays consistent
// with what the browser sees.
//
// Pinned image: ZITADEL v4 (July 2025) defaults to LoginV2 (a separate
// Next.js app not bundled in this image). v2.71.2 is the last v2 release
// shipping the embedded /ui/login as the default redirect target.
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
    .WithHttpEndpoint(name: "console", targetPort: 8080)
    .WaitFor(zitadelDb);

// EXTERNALPORT has to match the published host port so the issuer URL ZITADEL
// stamps into JWTs (and the redirect URLs it builds) line up with what the
// browser actually sees. Done as a second pass because the endpoint reference
// only exists after WithHttpEndpoint above.
var zitadelEndpoint = zitadel.GetEndpoint("console");
zitadel.WithEnvironment("ZITADEL_EXTERNALPORT", zitadelEndpoint.Property(EndpointProperty.Port));
var zitadelPublicUrl = ReferenceExpression.Create(
    $"http://localhost:{zitadelEndpoint.Property(EndpointProperty.Port)}");

// Provisions the OIDC app + test user, writes per-key client secret files
// into appSecrets where api and web pick them up. INTERNAL_URL points at the
// docker-network sibling (container-internal port 8080); PUBLIC_URL drives
// the issuer claim and browser redirects, so it matches the host port. The
// redirect / post-logout URIs are passed in so bootstrap.sh can register
// whatever port Aspire assigned to web.
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
    // Wait on postgres (via zitadelDb), not zitadel itself: in Aspire 13.3,
    // WaitFor needs the target's health checks to flip Healthy, and a
    // container with no health checks stays at Unknown forever. Postgres
    // ships with a working health check; zitadel doesn't (and our attempt
    // at WithHttpHealthCheck wasn't actually probed). Bootstrap.sh has its
    // own PAT-file wait loop that handles the "zitadel container started
    // but FirstInstance not finished" race, so we don't actually need
    // Aspire to gate on zitadel readiness here.
    .WaitFor(zitadelDb);

// Deterministic per-worktree ports for temporal (grpc) and temporal-ui.
// Aspire's WithEndpoint with scheme:"tcp" doesn't reliably publish to a
// random host port the way WithHttpEndpoint does — we have to pin one.
// Pinning to the canonical 7233/8233 would collide between parallel
// worktrees, so derive an offset from worktreeId: same worktree gets the
// same ports across restarts, different worktrees get different ports.
// Range deliberately above 16000 to avoid ranges where dev tooling tends
// to squat (and well clear of compose's pinned 7233/8233).
var temporalPortOffset = (int)(uint.Parse(worktreeId[..4], System.Globalization.NumberStyles.HexNumber) % 1000);
var temporalGrpcPort = 17000 + temporalPortOffset;
var temporalUiPort = 18000 + temporalPortOffset;

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
    .WaitFor(temporalDb)
    .WaitFor(temporalVisibilityDb);

var temporalUi = builder.AddContainer("temporal-ui", "temporalio/ui", "latest")
    .WithHttpEndpoint(port: temporalUiPort, targetPort: 8080)
    .WithEnvironment("TEMPORAL_ADDRESS", ReferenceExpression.Create(
        $"{temporal.GetEndpoint("grpc").Property(EndpointProperty.Host)}:{temporal.GetEndpoint("grpc").Property(EndpointProperty.TargetPort)}"))
    .WaitFor(temporal);

// CORS origin set after WithHttpEndpoint so the endpoint reference exists.
// temporal-ui's dashboard JS makes XHRs back to its own origin; with a random
// host port we have to point CORS at the assigned URL.
var temporalUiEndpoint = temporalUi.GetEndpoint("http");
temporalUi.WithEnvironment("TEMPORAL_CORS_ORIGINS", ReferenceExpression.Create(
    $"http://localhost:{temporalUiEndpoint.Property(EndpointProperty.Port)}"));

// api / worker run as projects (in-process on the host), so they reach
// temporal via the published host port — which is now random because the
// endpoint above is unpinned. EndpointProperty.Port is host-published;
// TargetPort would give the container-internal 7233 and miss the actual
// listener. temporal-ui above stays on TargetPort because it's a sibling
// container talking over Docker DNS.
var temporalConnString = ReferenceExpression.Create(
    $"{temporal.GetEndpoint("grpc").Property(EndpointProperty.Host)}:{temporal.GetEndpoint("grpc").Property(EndpointProperty.Port)}");

// API owns migrations + seed; worker waits on API health before querying.
var api = builder.AddProject<Projects.api>("api")
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(images).WaitFor(storage)
    .WithEnvironment("REVIEWS_APP_SECRETS_DIR", appSecrets)
    .WithEnvironment("ConnectionStrings__temporal", temporalConnString)
    .WaitFor(temporal)
    .WaitForCompletion(zitadelBootstrap);

var workerService = builder.AddProject<Projects.worker>("worker")
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(images).WaitFor(storage)
    .WithEnvironment("ConnectionStrings__temporal", temporalConnString)
    .WaitFor(temporal)
    .WaitFor(api);

// Web's full config — endpoint was declared up top so bootstrap could
// reference the assigned port; everything else (api ref, env, OTLP) is
// chained on now that those resources exist.
web
    .WithReference(api).WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEnvironment("REVIEWS_APP_SECRETS_DIR", appSecrets)
    // In Aspire both URLs collapse to localhost (web runs in-process, so it
    // reaches ZITADEL via the published host port); compose differs because
    // that route crosses container boundaries.
    .WithEnvironment("ZITADEL_PUBLIC_URL", zitadelPublicUrl)
    .WithEnvironment("ZITADEL_INTERNAL_URL", zitadelPublicUrl)
    .WithEnvironment("REDIS_URL", redisUrl)
    .WithEnvironment("REDIS_TLS_INSECURE", "true")
    .WithEnvironment("SESSION_SECRET", "dev-only-session-secret-rotate-in-prod")
    .WithOtlpExporter()
    .WaitForCompletion(zitadelBootstrap);

api.WithEnvironment("WEB_ORIGIN", web.GetEndpoint("http"));

builder.Build().Run();
