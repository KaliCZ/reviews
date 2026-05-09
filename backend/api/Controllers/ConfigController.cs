using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Reviews.Api.Models;

namespace Reviews.Api.Controllers;

public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";

    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = false)]
    public string SiteKey { get; init; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(AllowEmptyStrings = false)]
    public string SecretKey { get; init; } = string.Empty;
}

// Public bootstrap config for the SPA — keeps a single browser bundle across
// environments. Anything env-specific the SPA needs goes here.
[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class ConfigController(IOptions<TurnstileOptions> turnstile) : ControllerBase
{
    [HttpGet]
    public ActionResult<ConfigResponse> Get() =>
        Ok(new ConfigResponse(turnstile.Value.SiteKey));
}
