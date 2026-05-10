using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Reviews.Api.Auth;

// OIDC `auth_time` is seconds-since-epoch of the IdP's user-authentication
// event (not token issuance). Step-up checks read it to require a fresh
// password prompt for high-impact actions like delete. ZITADEL emits it in
// id_tokens but not in JWT access tokens, so the BFF pins a freshness signal
// onto the session at /auth/callback time and forwards it as X-Auth-Time;
// we fall back to that header when the JWT itself doesn't carry the claim.
public static class RecentAuth
{
    public static readonly TimeSpan DeleteFreshness = TimeSpan.FromSeconds(10);

    // BFF-forwarded freshness header. Trusted because the API only accepts
    // traffic from the BFF, which strips/overrides this header on every
    // proxied request — the SPA can't spoof it.
    public const string AuthTimeHeader = "X-Auth-Time";

    // Returns null on success, or a 401 result describing the re-auth requirement.
    // The SPA matches on the `error` body field to redirect through max_age login.
    public static IActionResult? RequireFresh(ClaimsPrincipal principal, TimeSpan freshness, HttpRequest request, HttpResponse response)
    {
        if (!TryReadAuthTime(principal, request, out var seconds))
            return Challenge(response, "auth_time signal missing");

        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var age = DateTimeOffset.UtcNow - authenticatedAt;
        if (age <= freshness) return null;

        return Challenge(response, $"Last sign-in was {(int)age.TotalSeconds}s ago; recent authentication required.");
    }

    private static bool TryReadAuthTime(ClaimsPrincipal principal, HttpRequest request, out long seconds)
    {
        var claim = principal.FindFirstValue("auth_time");
        if (claim is not null && long.TryParse(claim, out seconds)) return true;

        var header = request.Headers[AuthTimeHeader].ToString();
        if (!string.IsNullOrEmpty(header) && long.TryParse(header, out seconds)) return true;

        seconds = 0;
        return false;
    }

    private static IActionResult Challenge(HttpResponse response, string description)
    {
        response.Headers["WWW-Authenticate"] =
            $"Bearer error=\"reauth_required\", error_description=\"{description}\"";
        return new ObjectResult(new { error = "reauth_required", description })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
        };
    }
}
