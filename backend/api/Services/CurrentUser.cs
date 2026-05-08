using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Reviews.Api.Services;

// Resolves the current OIDC user from the request's ClaimsPrincipal. The
// ZITADEL `sub` claim is a string (numeric per ZITADEL's defaults); we hash
// it into a Guid so the rest of the system can keep using Guid PKs without
// caring how the IdP shapes its user ids. The mapping is deterministic, so
// the same `sub` always yields the same Guid — safe to use as a stable
// AuthorId/VoterId across requests and restarts.
//
// Display name comes from `name` / `preferred_username` claims, with a
// fallback to email-local-part. ZITADEL's default profile scope yields all
// three.
public interface ICurrentUser
{
    Guid? Id { get; }
    string? Name { get; }
    string? Sub { get; }
    bool IsAuthenticated { get; }
}

public class CurrentUser : ICurrentUser
{
    public Guid? Id { get; }
    public string? Name { get; }
    public string? Sub { get; }
    public bool IsAuthenticated => Id is not null;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated is not true) return;

        Sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
              ?? user.FindFirstValue("sub");
        if (string.IsNullOrEmpty(Sub)) return;

        Id = SubToGuid(Sub);
        Name = user.FindFirstValue("name")
            ?? user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Name)
            ?? user.FindFirstValue(ClaimTypes.Email)?.Split('@')[0];
    }

    public static Guid SubToGuid(string sub)
    {
        // SHA-256 → take first 16 bytes → Guid. Stable, collision-resistant
        // enough for application-level identifiers, and reversible only with
        // brute force which doesn't matter here.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sub));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
