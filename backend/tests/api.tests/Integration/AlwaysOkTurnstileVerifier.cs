using Reviews.Api.Services;

namespace Reviews.Api.Tests.Integration;

// Skips the live Cloudflare call — integration tests assert workflow + DB behaviour.
public sealed class AlwaysOkTurnstileVerifier : ITurnstileVerifier
{
    public Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct) =>
        Task.FromResult(true);
}
