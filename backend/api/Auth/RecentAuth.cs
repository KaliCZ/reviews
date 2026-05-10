using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Reviews.Api.Auth;

// OIDC `auth_time` is seconds-since-epoch of the IdP's user-authentication
// event (not token issuance). Step-up checks read it to require a fresh
// password prompt for high-impact actions like delete. ZITADEL emits it in
// JWT access tokens.
public static class RecentAuth
{
    public static readonly TimeSpan DeleteFreshness = TimeSpan.FromMinutes(5);

    // Returns null on success, or a 401 result describing the re-auth requirement.
    // The SPA matches on the `error` body field to redirect through max_age login.
    public static IActionResult? RequireFresh(ClaimsPrincipal principal, TimeSpan freshness, HttpResponse response)
    {
        var raw = principal.FindFirstValue("auth_time");
        if (raw is null || !long.TryParse(raw, out var seconds))
            return Challenge(response, "auth_time claim missing");

        var authenticatedAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var age = DateTimeOffset.UtcNow - authenticatedAt;
        if (age <= freshness) return null;

        return Challenge(response, $"Last sign-in was {(int)age.TotalSeconds}s ago; recent authentication required.");
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
