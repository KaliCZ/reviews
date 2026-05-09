using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Reviews.Api.Models;
using StrongTypes;

namespace Reviews.Api.Controllers;

// Strongly-typed view of the bootstrap config the SPA needs from the API.
// Bound from the `Turnstile` configuration section at startup; ValidateOnStart
// + DataAnnotations make a missing SiteKey fail at boot rather than serving
// the SPA an empty key.
public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";

    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = false)]
    public string SiteKey { get; init; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = false)]
    public string SecretKey { get; init; } = string.Empty;
}

// Public bootstrap config for the SPA — keeps a single browser bundle across
// environments (dev / compose / prod) without env-specific builds. Anything
// that needs to differ between environments goes here.
//
// Configuration is provided by the orchestration layer (appsettings.json,
// docker-compose env, Aspire). The controller doesn't default — if Turnstile
// isn't configured the app fails at startup, surfaced via the validated
// options binding.
[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class ConfigController(IOptions<TurnstileOptions> turnstile) : ControllerBase
{
    [HttpGet]
    public ActionResult<ConfigResponse> Get() => Ok(new ConfigResponse
    {
        TurnstileSiteKey = turnstile.Value.SiteKey.ToNonEmpty()
    });
}
