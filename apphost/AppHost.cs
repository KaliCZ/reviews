using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var zitadelMasterkey = builder.AddParameter("zitadel-masterkey", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()
    .WithBindMount("../infra/postgres-init.sh", "/docker-entrypoint-initdb.d/postgres-init.sh", isReadOnly: true)
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

var zitadel = builder.AddContainer("zitadel", "ghcr.io/zitadel/zitadel", "latest")
    .WithArgs("start-from-init", "--masterkeyFromEnv", "--tlsMode", "disabled")
    .WithEnvironment("ZITADEL_MASTERKEY", zitadelMasterkey)
    .WithEnvironment("ZITADEL_EXTERNALSECURE", "false")
    .WithEnvironment("ZITADEL_EXTERNALDOMAIN", "localhost")
    .WithEnvironment("ZITADEL_EXTERNALPORT", "8080")
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

var workerService = builder.AddProject<Projects.worker>("worker")
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(cache).WaitFor(cache)
    .WithEnvironment("ConnectionStrings__temporal", temporalConnString)
    .WaitFor(temporal);

var api = builder.AddProject<Projects.api>("api")
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(cache).WaitFor(cache)
    .WithReference(images).WaitFor(storage)
    .WithEnvironment("ConnectionStrings__temporal", temporalConnString)
    .WaitFor(temporal)
    .WaitFor(workerService);

var web = builder.AddJavaScriptApp("web", "../web", "start")
    .WithReference(api).WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithHttpEndpoint(env: "PORT", targetPort: 4200)
    .WithExternalHttpEndpoints();

api.WithEnvironment("WEB_ORIGIN", web.GetEndpoint("http"));

builder.Build().Run();
