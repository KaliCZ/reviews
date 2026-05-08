using Reviews.Shared;
using Reviews.Worker;
using Temporalio.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient(connectionName: "cache");

var temporalAddress = builder.Configuration.GetConnectionString("temporal")
    ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

builder.Services
    .AddTemporalClient(options =>
    {
        options.TargetHost = temporalAddress;
        options.Namespace = "default";
    })
    .AddHostedTemporalWorker(
        clientTargetHost: temporalAddress,
        clientNamespace: "default",
        taskQueue: IncrementCounterWorkflow.TaskQueue)
    .AddWorkflow<IncrementCounterWorkflow>()
    .AddScopedActivities<CounterActivities>();

var host = builder.Build();
host.Run();
