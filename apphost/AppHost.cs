var builder = DistributedApplication.CreateBuilder(args);

var postgresPassword = builder.AddParameter("postgres-password", secret: true);
var zitadelMasterkey = builder.AddParameter("zitadel-masterkey", secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithDataVolume()
    .WithPgAdmin();

var reviewsDb = postgres.AddDatabase("reviews");
var zitadelDb = postgres.AddDatabase("zitadel-db", databaseName: "zitadel");

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
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_HOST", postgres.Resource.PrimaryEndpoint.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.Host))
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_PORT", postgres.Resource.PrimaryEndpoint.Property(Aspire.Hosting.ApplicationModel.EndpointProperty.TargetPort))
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_DATABASE", "zitadel")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_USERNAME", "postgres")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_PASSWORD", postgresPassword)
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_USER_SSL_MODE", "disable")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_USERNAME", "postgres")
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_PASSWORD", postgresPassword)
    .WithEnvironment("ZITADEL_DATABASE_POSTGRES_ADMIN_SSL_MODE", "disable")
    .WithHttpEndpoint(name: "console", port: 8080, targetPort: 8080)
    .WaitFor(zitadelDb);

var api = builder.AddProject<Projects.api>("api")
    .WithReference(cache).WaitFor(cache)
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(images).WaitFor(storage);

var web = builder.AddJavaScriptApp("web", "../web", "start")
    .WithReference(api).WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithHttpEndpoint(env: "PORT", targetPort: 4200)
    .WithExternalHttpEndpoints();

api.WithEnvironment("WEB_ORIGIN", web.GetEndpoint("http"));

builder.Build().Run();
