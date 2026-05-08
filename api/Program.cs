using Reviews.Shared;
using Temporalio.Client;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var temporalAddress = builder.Configuration.GetConnectionString("temporal")
    ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

builder.Services.AddSingleton<ITemporalClient>(_ =>
    TemporalClient.ConnectAsync(new(temporalAddress) { Namespace = "default" })
        .ConfigureAwait(false).GetAwaiter().GetResult());

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

app.MapPost("/api/hello", async (HelloRequest body, ITemporalClient temporal) =>
{
    var by = body.By <= 0 ? 1 : body.By;
    var handle = await temporal.StartWorkflowAsync(
        (IncrementCounterWorkflow wf) => wf.RunAsync(by),
        new(id: $"increment-{Guid.NewGuid():N}", taskQueue: IncrementCounterWorkflow.TaskQueue));

    var count = await handle.GetResultAsync();
    return Results.Ok(new { message = "Incremented via Temporal", count });
});

app.Run();

internal record HelloRequest(int By);
