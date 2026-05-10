using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Reviews.Api.Tests;

// Always-authenticates handler swapped in for JwtBearer in integration tests.
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public new const string Scheme = "Test";

    // Header lets a test override `auth_time` to exercise the reauth gate.
    // The literal value "omit" suppresses the claim entirely so tests can
    // exercise the BFF-forwarded X-Auth-Time fallback path in RecentAuth.
    public const string AuthTimeHeader = "X-Test-Auth-Time";
    public const string AuthTimeOmitMarker = "omit";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var headerValue = Request.Headers.TryGetValue(AuthTimeHeader, out var v) ? v.ToString() : null;
        var omitAuthTime = string.Equals(headerValue, AuthTimeOmitMarker, StringComparison.Ordinal);
        var authTime = !omitAuthTime && long.TryParse(headerValue, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
            new("sub", "00000000-0000-0000-0000-000000000001"),
            new("name", "Test User"),
        };
        if (!omitAuthTime) claims.Add(new Claim("auth_time", authTime.ToString()));

        var identity = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
