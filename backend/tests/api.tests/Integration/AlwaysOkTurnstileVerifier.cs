using Reviews.Api.Services;

namespace Reviews.Api.Tests.Integration;

// ITurnstileVerifier replacement for tests. Real Cloudflare verification
// would need a network call to challenges.cloudflare.com on every Submit.
// The integration tests assert workflow + DB behaviour; the Turnstile
// integration itself is exercised by the dedicated TurnstileVerifier path
// (not under test here). This stub returns true regardless of input.
public sealed class AlwaysOkTurnstileVerifier : ITurnstileVerifier
{
    public Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct) =>
        Task.FromResult(true);
}
