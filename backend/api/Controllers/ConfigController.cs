using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Reviews.Api.Models;

namespace Reviews.Api.Controllers;

// Public bootstrap config for the SPA — keeps a single browser bundle across
// environments (dev / compose / prod) without env-specific builds. Anything
// that needs to differ between environments goes here.
[ApiController]
[AllowAnonymous]
[Route("api/[controller]")]
public class ConfigController(IConfiguration config) : ControllerBase
{
    [HttpGet]
    public ActionResult<ConfigResponse> Get() => Ok(new ConfigResponse(
        TurnstileSiteKey: config["Turnstile:SiteKey"] ?? "1x00000000000000000000AA"));
}
