using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Reviews.Infrastructure;

// Applies pending EF Core migrations on startup, guarded by a Postgres
// advisory lock so concurrent callers don't race. We run this from the API
// because the API is the natural schema owner; the worker waits for the API's
// /health to come up before starting (Aspire WaitFor / compose service_healthy)
// so by the time the worker queries, the schema is ready.
//
// In production you'd typically disable this (Migrations:AutoApply=false) and
// run an EF bundle as a deploy step. For the kickoff there's no deploy
// pipeline, so auto-apply on boot is the simplest correct option.
public static class MigrationRunner
{
    // 64-bit signed key for pg_try_advisory_lock — pick anything stable; if a
    // future component picks a colliding key, just change them both.
    private const long LockKey = 738_293_741_829_010L;

    public static async Task ApplyAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReviewsDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<ReviewsDbContext>>();

        // Open a single connection for the lock + migrations. The lock is held
        // for the lifetime of this connection (session-scoped advisory lock),
        // so EF's own connection management inside Migrate() doesn't matter —
        // any concurrent caller will block on pg_advisory_lock until we
        // release on dispose.
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using (var lockCmd = conn.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@key)";
                var p = lockCmd.CreateParameter();
                p.ParameterName = "key";
                p.Value = LockKey;
                lockCmd.Parameters.Add(p);
                await lockCmd.ExecuteNonQueryAsync(ct);
            }

            log.LogInformation("Acquired migration advisory lock; applying pending migrations");
            await db.Database.MigrateAsync(ct);
            log.LogInformation("Migrations applied");
        }
        finally
        {
            // Releasing the lock isn't strictly required (closing the
            // connection drops it) but being explicit avoids surprises.
            await using var unlock = conn.CreateCommand();
            unlock.CommandText = "SELECT pg_advisory_unlock(@key)";
            var p = unlock.CreateParameter();
            p.ParameterName = "key";
            p.Value = LockKey;
            unlock.Parameters.Add(p);
            try { await unlock.ExecuteNonQueryAsync(ct); } catch { /* best effort */ }
            await conn.CloseAsync();
        }
    }
}
