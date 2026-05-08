using Reviews.Shared;
using Temporalio.Activities;
using Temporalio.Client;
using Temporalio.Testing;
using Temporalio.Worker;

namespace Reviews.Shared.Tests;

public class IncrementCounterWorkflowTests
{
    [Fact]
    public async Task RunAsync_invokes_increment_activity_with_by_and_returns_its_result()
    {
        await using var env = await WorkflowEnvironment.StartTimeSkippingAsync();

        var stub = new RecordingActivities(returns: 42L);

        using var worker = new TemporalWorker(
            env.Client,
            new TemporalWorkerOptions(IncrementCounterWorkflow.TaskQueue)
                .AddWorkflow<IncrementCounterWorkflow>()
                .AddAllActivities(stub));

        var result = await worker.ExecuteAsync(async () =>
            await env.Client.ExecuteWorkflowAsync(
                (IncrementCounterWorkflow wf) => wf.RunAsync(7),
                new(id: $"test-{Guid.NewGuid():N}", taskQueue: IncrementCounterWorkflow.TaskQueue)));

        Assert.Equal(42L, result);
        Assert.Equal(7, stub.LastBy);
    }

    private class RecordingActivities(long returns)
    {
        public int LastBy { get; private set; }

        [Activity(IncrementCounterWorkflow.IncrementActivity)]
        public Task<long> IncrementAsync(int by)
        {
            LastBy = by;
            return Task.FromResult(returns);
        }
    }
}
