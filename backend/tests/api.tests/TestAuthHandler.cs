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
    public const string AuthTimeHeader = "X-Test-Auth-Time";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authTime = Request.Headers.TryGetValue(AuthTimeHeader, out var v)
            && long.TryParse(v.ToString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
            new Claim("sub", "00000000-0000-0000-0000-000000000001"),
            new Claim("name", "Test User"),
            new Claim("auth_time", authTime.ToString()),
        };
        var identity = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
