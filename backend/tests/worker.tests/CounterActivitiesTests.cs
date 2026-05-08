using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Reviews.Worker.Tests;

public class CounterActivitiesTests : IAsyncLifetime
{
    private readonly RedisContainer redis = new RedisBuilder("redis:8-alpine").Build();
    private IConnectionMultiplexer conn = null!;

    public async ValueTask InitializeAsync()
    {
        await redis.StartAsync(TestContext.Current.CancellationToken);
        conn = await ConnectionMultiplexer.ConnectAsync(redis.GetConnectionString());
    }

    public async ValueTask DisposeAsync()
    {
        conn.Dispose();
        await redis.DisposeAsync();
    }

    [Fact]
    public async Task IncrementAsync_persists_running_total_to_redis()
    {
        var sut = new CounterActivities(conn, NullLogger<CounterActivities>.Instance);

        var first = await sut.IncrementAsync(3);
        var second = await sut.IncrementAsync(2);

        Assert.Equal(3, first);
        Assert.Equal(5, second);

        var stored = (string?)await conn.GetDatabase().StringGetAsync("hello:count");
        Assert.Equal("5", stored);
    }
}
