using Microsoft.Extensions.Diagnostics.HealthChecks;
using Temporalio.Client;

namespace Microsoft.Extensions.Hosting;

internal sealed class TemporalHealthCheck(ITemporalClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var healthy = await client.Connection.CheckHealthAsync(
                options: new Temporalio.Client.RpcOptions { CancellationToken = cancellationToken });
            return healthy
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Temporal frontend reported unhealthy");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Temporal frontend unreachable", ex);
        }
    }
}
