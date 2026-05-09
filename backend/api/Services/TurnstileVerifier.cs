using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Reviews.Api.Controllers;

namespace Reviews.Api.Services;

// Cloudflare Turnstile server-side verification. The frontend renders the
// widget and ends up with a token; the API sends (token, secret) to
// challenges.cloudflare.com/turnstile/v0/siteverify and gates the request on
// the response.
//
// Dev uses Cloudflare's documented test keys:
//   - secret: 1x0000000000000000000000000000000AA  (always passes)
//   - sitekey: 1x00000000000000000000AA            (always passes, visible widget)
// so we don't have to hand out a real Cloudflare key for local development.
// Production: swap via configuration; nothing in the code path changes.
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct);
}

public class TurnstileVerifier(
    HttpClient http,
    IOptions<TurnstileOptions> options,
    ILogger<TurnstileVerifier> logger) : ITurnstileVerifier
{
    private static readonly Uri SiteVerifyUrl =
        new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct)
    {
        // TurnstileOptions binding is validated at startup (ValidateOnStart),
        // so by the time this runs SecretKey is guaranteed non-empty.
        var secret = options.Value.SecretKey;

        var form = new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = token
        };
        if (!string.IsNullOrWhiteSpace(remoteIp))
            form["remoteip"] = remoteIp;

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(SiteVerifyUrl, content, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(ct);
            if (body?.Success is true) return true;

            logger.LogWarning(
                "Turnstile rejected token: errors={Errors}",
                body?.ErrorCodes is null ? "(none)" : string.Join(",", body.ErrorCodes));
            return false;
        }
        catch (Exception ex)
        {
            // A failing verification call is not the same as a failing token —
            // log loudly so an outage isn't mistaken for spam, but still deny.
            logger.LogError(ex, "Turnstile verification call failed");
            return false;
        }
    }

    private sealed class SiteVerifyResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("error-codes")] public string[]? ErrorCodes { get; set; }
    }
}
