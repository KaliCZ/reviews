using Reviews.Shared;
using StackExchange.Redis;
using Temporalio.Activities;

namespace Reviews.Worker;

public class CounterActivities
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CounterActivities> _logger;

    public CounterActivities(IConnectionMultiplexer redis, ILogger<CounterActivities> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    [Activity(IncrementCounterWorkflow.IncrementActivity)]
    public async Task<long> IncrementAsync(int by)
    {
        var db = _redis.GetDatabase();
        var newCount = await db.StringIncrementAsync("hello:count", by);
        _logger.LogInformation("Incremented hello:count by {By} -> {NewCount}", by, newCount);
        return newCount;
    }
}
