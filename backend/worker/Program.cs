using Microsoft.EntityFrameworkCore;
using Reviews.Infrastructure;
using Reviews.Shared;
using Reviews.Worker;
using Temporalio.Extensions.Hosting;

// WebApplication.CreateBuilder gives us the same generic host the worker had
// before, plus Kestrel — so we can expose /alive and /health for orchestrator
// probes (compose healthcheck today, Kubernetes liveness/readiness later).
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Worker uses the typed DbContext for DB writes from activities. Schema is
// owned by the API (it runs the migrations on startup behind an advisory
// lock); the worker just connects and uses what's there.
builder.AddNpgsqlDbContext<ReviewsDbContext>("reviews", configureDbContextOptions: opts =>
{
    opts.UseNpgsql(o => o.MigrationsHistoryTable("__ef_migrations_history", ReviewsDbContext.Schema));
});
builder.AddRedisClient(connectionName: "cache");

var temporalAddress = builder.Configuration.GetConnectionString("temporal")
    ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

// AddTemporalClient registers a *lazy* ITemporalClient — the connection isn't
// opened until first use. AddHostedTemporalWorker's 1-arg overload picks it up
// from DI. Compose gates worker startup on Temporal's healthcheck, and Aspire
// uses WaitFor(), so by the time the worker polls Temporal is actually ready.
//
// All review workflows share the "reviews" task queue. Adding a new workflow
// is two lines here (AddWorkflow + register its activities if not already in
// ReviewActivities).
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

builder.Services.AddHealthChecks().AddInfraHealthChecks();

var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
