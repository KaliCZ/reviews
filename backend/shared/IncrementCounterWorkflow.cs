using Temporalio.Workflows;

namespace Reviews.Shared;

[Workflow]
public class IncrementCounterWorkflow
{
    public const string TaskQueue = "reviews";
    public const string IncrementActivity = "Increment";

    [WorkflowRun]
    public async Task<long> RunAsync(int by)
    {
        return await Workflow.ExecuteActivityAsync<long>(
            IncrementActivity,
            new object[] { by },
            new()
            {
                StartToCloseTimeout = TimeSpan.FromSeconds(10)
            });
    }
}
