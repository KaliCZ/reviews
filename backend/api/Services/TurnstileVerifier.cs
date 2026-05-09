using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Reviews.Api.Controllers;

namespace Reviews.Api.Services;

// Cloudflare Turnstile server-side verification. Dev uses Cloudflare's
// documented test keys (secret 1x0000...AA, sitekey 1x00...AA) which
// always pass.
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
        new Uri("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<bool> VerifyAsync(string token, string? remoteIp, CancellationToken ct)
    {
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
            // Network/Cloudflare outage — log loudly, still deny.
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
