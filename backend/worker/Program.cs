using Microsoft.EntityFrameworkCore;
using Reviews.Infrastructure;
using Reviews.Shared;
using StrongTypes.EfCore;
using Temporalio.Extensions.Hosting;

namespace Reviews.Worker;

public class Program
{
    public static async Task Main(string[] args)
    {
        // WebApplication so we get Kestrel for /alive and /health probes.
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();
        // API owns the schema (runs migrations behind an advisory lock); worker just connects.
        builder.AddNpgsqlDbContext<ReviewsDbContext>("reviews", configureDbContextOptions: opts =>
            opts.UseNpgsql().UseStrongTypes());
        builder.AddRedisClient(connectionName: "cache");

        var temporalAddress = builder.Configuration.GetConnectionString("temporal")
            ?? throw new InvalidOperationException("ConnectionStrings:temporal not configured");

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
        await app.RunAsync();
    }
}
