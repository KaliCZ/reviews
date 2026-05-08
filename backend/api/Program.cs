var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource(connectionName: "reviews");
builder.AddRedisClient(connectionName: "cache");

var temporalAddress = builder.Configuration.GetConnectionString("temporal")
    ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

// Lazy client: doesn't open the gRPC connection until first use, so a slow or
// briefly-unavailable Temporal at startup doesn't crash the API. Reconnects
// automatically on subsequent calls if a connection is later lost.
builder.Services.AddTemporalClient(options =>
{
    options.TargetHost = temporalAddress;
    options.Namespace = "default";
});

builder.Services.AddHealthChecks().AddInfraHealthChecks();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration["WEB_ORIGIN"] ?? "http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.MapControllers();

app.Run();
