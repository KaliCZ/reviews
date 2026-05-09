using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var zitadelMasterkey = builder.AddParameter("zitadel-masterkey", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()
    .WithBindMount("../../infra/postgres-init.sh", "/docker-entrypoint-initdb.d/postgres-init.sh", isReadOnly: true)
    .WithEnvironment("POSTGRES_MULTIPLE_DATABASES", "reviews,zitadel,temporal,temporal_visibility")
    .WithPgAdmin();

var reviewsDb = postgres.AddDatabase("reviews");
var zitadelDb = postgres.AddDatabase("zitadel-db", databaseName: "zitadel");
var temporalDb = postgres.AddDatabase("temporal-db", databaseName: "temporal");
var temporalVisibilityDb = postgres.AddDatabase("temporal-visibility-db", databaseName: "temporal_visibility");

var cache = builder.AddRedis("cache")
    .WithRedisInsight();

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var images = storage.AddBlobs("images");

// Bind-mount paths used to share state between zitadel and the bootstrap
// container, plus the per-key secret files the api reads via KeyPerFile.
// Host directories — Aspire's project resources don't support container-style
// bind mounts, but they do run on the host so they can read these directly
// via path. The web (JS) project doesn't get container bind-mounts either,
// same reason.
//
// The KeyPerFile config provider in api/Program.cs reads /run/secrets/* in
// containerized runs; in Aspire we point the same convention at the host
// path via API_SECRETS_DIR.
const string zitadelSecrets = "../../infra/zitadel/.secrets";
const string appSecrets = "../../infra/zitadel/.app-secrets";
var appSecretsAbs = Path.GetFullPath(appSecrets);

// Pinned: ZITADEL v4 (July 2025) made LoginV2 the default; the v2 login UI
// is a separate Next.js app not bundled in this image. v2.71.2 is the last
// v2 release that ships the embedded /ui/login UI as the default OIDC
// redirect target.
var zitadel = builder.AddContainer("zitadel", "ghcr.io/zitadel/zitadel", "v2.71.2")
    // start-from-init runs the FirstInstance bootstrap (default org + admin
    // user + bootstrap service-account PAT) defined in steps.yaml.
    .WithArgs("start-from-init", "--masterkeyFromEnv", "--steps", "/steps.yaml")
    .WithBindMount("../../infra/zitadel/steps.yaml", "/steps.yaml", isReadOnly: true)
    .WithBindMount(zitadelSecrets, "/zitadel-secrets")
    .WithEnvironment("ZITADEL_MASTERKEY", zitadelMasterkey)
    .WithEnvironment("ZITADEL_EXTERNALSECURE", "false")
    .WithEnvironment("ZITADEL_EXTERNALDOMAIN", "localhost")
    .WithEnvironment("ZITADEL_EXTERNALPORT", "8080")
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
    .WithHttpEndpoint(name: "console", port: 8080, targetPort: 8080)
    .WaitFor(zitadelDb);

// One-shot bootstrap: provisions the OIDC app + a test user via mgmt API
// using the PAT zitadel just wrote. Outputs ZITADEL_CLIENT_ID/SECRET into
// the appSecrets bind-mount; api and web read from there at startup.
var zitadelBootstrap = builder.AddContainer("zitadel-bootstrap", "curlimages/curl", "8.10.1")
    .WithEntrypoint("/bin/sh")
    .WithArgs("-c", "/bootstrap.sh")
    .WithBindMount("../../infra/zitadel/bootstrap.sh", "/bootstrap.sh", isReadOnly: true)
    .WithBindMount(zitadelSecrets, "/zitadel-secrets", isReadOnly: true)
    .WithBindMount(appSecrets, "/app-secrets")
    .WithEnvironment("ZITADEL_INTERNAL_URL", "http://localhost:8080")
    .WithEnvironment("ZITADEL_PUBLIC_URL", "http://localhost:8080")
    .WaitFor(zitadel);

var temporal = builder.AddContainer("temporal", "temporalio/auto-setup", "latest")
    .WithEndpoint(name: "grpc", port: 7233, targetPort: 7233, scheme: "tcp")
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
    .WithHttpEndpoint(port: 8233, targetPort: 8080)
    .WithEnvironment("TEMPORAL_ADDRESS", ReferenceExpression.Create(
        $"{temporal.GetEndpoint("grpc").Property(EndpointProperty.Host)}:{temporal.GetEndpoint("grpc").Property(EndpointProperty.TargetPort)}"))
    .WithEnvironment("TEMPORAL_CORS_ORIGINS", "http://localhost:8233")
    .WaitFor(temporal);

var temporalConnString = ReferenceExpression.Create(
    $"{temporal.GetEndpoint("grpc").Property(EndpointProperty.Host)}:{temporal.GetEndpoint("grpc").Property(EndpointProperty.TargetPort)}");

// API owns migrations + seed; worker waits for API health before it tries
// to query against the schema.
//
// API_SECRETS_DIR points the api's KeyPerFile config provider at the host
// folder where zitadel-bootstrap drops its per-key output files (one file
// per config key — see infra/zitadel/bootstrap.sh). That replaces the prior
// dotenv hack; the framework's KeyPerFile is the standard for "external
// secrets in a directory."
var api = builder.AddProject<Projects.api>("api")
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(images).WaitFor(storage)
    .WithEnvironment("API_SECRETS_DIR", appSecretsAbs)
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

var web = builder.AddJavaScriptApp("web", "../../web", "start")
    .WithReference(api).WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    // BFF (Express) reads ZITADEL_* env vars; bootstrap writes a kv file
    // alongside the per-key files for backwards compat with the JS side.
    .WithEnvironment("ZITADEL_ENV_FILE", Path.Combine(appSecretsAbs, "zitadel.env"))
    // In Aspire (no docker network in front of the BFF) both URLs collapse
    // to the same localhost reference; compose differs because that route
    // crosses container boundaries.
    .WithEnvironment("ZITADEL_PUBLIC_URL", "http://localhost:8080")
    .WithEnvironment("ZITADEL_INTERNAL_URL", "http://localhost:8080")
    .WithEnvironment("REDIS_URL", "redis://localhost:6379")
    .WithEnvironment("SESSION_SECRET", "dev-only-session-secret-rotate-in-prod")
    .WithHttpEndpoint(env: "PORT", targetPort: 4200)
    .WithExternalHttpEndpoints()
    .WaitForCompletion(zitadelBootstrap);

api.WithEnvironment("WEB_ORIGIN", web.GetEndpoint("http"));

builder.Build().Run();
