using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using StrongTypes;

namespace Reviews.Api.Services;

// The `sub` claim is hashed into a Guid (see SubToGuid) so AuthorId/VoterId
// stay Guid-shaped regardless of how the IdP formats user ids. Deterministic,
// so the same `sub` always yields the same Guid.
public interface ICurrentUserAccessor
{
    CurrentUser? User { get; }
}

public sealed record CurrentUser
{
    public required Guid Id { get; init; }
    public required NonEmptyString Sub { get; init; }
    public required NonEmptyString Name { get; init; }
}

public class CurrentUserAccessor : ICurrentUserAccessor
{
    public CurrentUser? User { get; }

    public CurrentUserAccessor(IHttpContextAccessor accessor)
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

        User = new CurrentUser
        {
            Id = SubToGuid(sub),
            Sub = sub.ToNonEmpty(),
            Name = name.ToNonEmpty(),
        };
    }

    public static Guid SubToGuid(string sub)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sub));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
