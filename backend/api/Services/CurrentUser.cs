using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using StrongTypes;

namespace Reviews.Api.Services;

// Resolves the current OIDC user from the request's ClaimsPrincipal. The
// ZITADEL `sub` claim is a string (numeric per ZITADEL's defaults); we hash
// it into a Guid so the rest of the system can keep using Guid PKs without
// caring how the IdP shapes its user ids. The mapping is deterministic, so
// the same `sub` always yields the same Guid — safe to use as a stable
// AuthorId/VoterId across requests and restarts.
//
// The User record is itself non-nullable in shape (every field required) — the
// nullability lives one level up: the accessor returns `User?`, and `null`
// means "no authenticated viewer". Callers narrow once and then have a
// fully-populated user instead of dancing around three nullable fields.
public interface ICurrentUser
{
    User? User { get; }
}

// Display name comes from `name` / `preferred_username` claims, with a
// fallback to email-local-part. ZITADEL's default profile scope yields all
// three. NonEmptyString on Sub/Name keeps the contract honest — if the IdP
// ever returns a blank claim we fail at construction rather than later.
public sealed record User
{
    public required Guid Id { get; init; }
    public required NonEmptyString Sub { get; init; }
    public required NonEmptyString Name { get; init; }
}

public class CurrentUser : ICurrentUser
{
    public User? User { get; }

    public CurrentUser(IHttpContextAccessor accessor)
    {
        var principal = accessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated is not true) return;

        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(sub)) return;

        var name = principal.FindFirstValue("name")
            ?? principal.FindFirstValue("preferred_username")
            ?? principal.FindFirstValue(ClaimTypes.Name)
            ?? principal.FindFirstValue(ClaimTypes.Email)?.Split('@')[0]
            ?? "Anonymous";

        User = new User
        {
            Id = SubToGuid(sub),
            Sub = sub.ToNonEmpty(),
            Name = name.ToNonEmpty(),
        };
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
