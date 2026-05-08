using Reviews.Shared;
using StackExchange.Redis;
using Temporalio.Activities;

namespace Reviews.Worker;

public class CounterActivities(IConnectionMultiplexer redis, ILogger<CounterActivities> logger)
{
    [Activity(IncrementCounterWorkflow.IncrementActivity)]
    public async Task<long> IncrementAsync(int by)
    {
        var db = redis.GetDatabase();
        var newCount = await db.StringIncrementAsync("hello:count", by);
        logger.LogInformation("Incremented hello:count by {By} -> {NewCount}", by, newCount);
        return newCount;
    }
}
