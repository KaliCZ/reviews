using Reviews.Shared;
using Reviews.Worker;
using Temporalio.Extensions.Hosting;

// WebApplication.CreateBuilder gives us the same generic host the worker had
// before, plus Kestrel — so we can expose /alive and /health for orchestrator
// probes (compose healthcheck today, Kubernetes liveness/readiness later).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource(connectionName: "reviews");
builder.AddRedisClient(connectionName: "cache");

var temporalAddress = builder.Configuration.GetConnectionString("temporal")
    ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

// AddTemporalClient registers a *lazy* ITemporalClient — the connection isn't
// opened until first use. AddHostedTemporalWorker's 1-arg overload picks it up
// from DI. Compose gates worker startup on Temporal's healthcheck, and Aspire
// uses WaitFor(), so by the time the worker polls Temporal is actually ready.
builder.Services
    .AddTemporalClient(options =>
    {
        options.TargetHost = temporalAddress;
        options.Namespace = "default";
    })
    .AddHostedTemporalWorker(taskQueue: IncrementCounterWorkflow.TaskQueue)
    .AddWorkflow<IncrementCounterWorkflow>()
    .AddScopedActivities<CounterActivities>();

builder.Services.AddHealthChecks().AddInfraHealthChecks();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
