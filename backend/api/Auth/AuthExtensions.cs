using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Reviews.Api.Services;

namespace Reviews.Api.Auth;

// JwtBearer's BackchannelHttpHandler — rewrites every outgoing call to the
// public ZITADEL URL (e.g. `http://localhost:8080`) to the docker-internal
// URL (e.g. `http://zitadel:8080`), AND sets the Host header to the public
// authority so ZITADEL's vhost routing (matching ZITADEL_EXTERNALDOMAIN)
// accepts the request.
//
// Why both? The discovery doc that ZITADEL returns embeds the public
// host in jwks_uri / token_endpoint / etc. JwtBearer then uses those
// strings verbatim for follow-up fetches. Inside the API container,
// `localhost` resolves to the container itself, not ZITADEL — so without
// the URL rewrite the JWKS fetch hits 127.0.0.1 and 404s. With the
// rewrite it goes via docker DNS to the ZITADEL container; the Host
// header reassures ZITADEL that this is still its public-facing vhost.
internal sealed class ZitadelDockerInternalHandler(Uri publicAuthority, Uri internalAuthority) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri
            && uri.Authority.Equals(publicAuthority.Authority, StringComparison.OrdinalIgnoreCase))
        {
            var rewritten = new UriBuilder(uri)
            {
                Scheme = internalAuthority.Scheme,
                Host = internalAuthority.Host,
                Port = internalAuthority.Port,
            }.Uri;
            request.RequestUri = rewritten;
            request.Headers.Host = publicAuthority.Authority;
        }
        return base.SendAsync(request, cancellationToken);
    }
}

public static class AuthExtensions
{
    public const string WriteRateLimitPolicy = "write";

    // JwtBearer against ZITADEL. The BFF (Angular SSR) terminates the OIDC
    // code flow and forwards an `Authorization: Bearer …` header to us; we
    // validate the JWT against ZITADEL's JWKS (auto-discovered from the
    // issuer's `.well-known/openid-configuration`).
    //
    // Issuer + audience come from the ZITADEL bootstrap step (a one-shot init
    // container provisions the OIDC app and writes the values to a shared
    // env file mounted at /run/secrets/zitadel.env in compose).
    public static IServiceCollection AddReviewsAuth(this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // ZITADEL_* names match what zitadel-bootstrap writes into
        // /run/secrets/zitadel.env; Auth:* are the config-style equivalents
        // for non-bootstrapped runs (Aspire dashboard, ad-hoc dotnet run).
        // ZITADEL_* names match what zitadel-bootstrap writes into
        // /run/secrets/zitadel.env; Auth:* are the config-style equivalents.
        var issuer = config["Auth:Issuer"] ?? config["ZITADEL_ISSUER"]
            ?? throw new InvalidOperationException("Auth issuer not configured (set Auth:Issuer or ZITADEL_ISSUER)");
        var audience = config["Auth:Audience"] ?? config["ZITADEL_CLIENT_ID"]
            ?? throw new InvalidOperationException("Auth audience not configured (set Auth:Audience or ZITADEL_CLIENT_ID)");

        // The IdP's public URL (the issuer in JWTs) and its docker-internal
        // URL might differ (compose: `http://localhost:8080` vs
        // `http://zitadel:8080`). When they do, every backchannel call from
        // JwtBearer routes through the rewriting handler above; otherwise
        // we don't bother and JwtBearer talks to the issuer directly.
        var internalAuthority = config["Auth:InternalAuthority"] ?? config["ZITADEL_INTERNAL_URL"];
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuer;
                options.Audience = audience;
                // Dev / compose run ZITADEL on plain HTTP. Toggle via config
                // so real prod (HTTPS issuer) keeps the default-on guard.
                options.RequireHttpsMetadata = config.GetValue("Auth:RequireHttps", !env.IsDevelopment());
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    NameClaimType = "name"
                };
                if (!string.IsNullOrEmpty(internalAuthority)
                    && !string.Equals(internalAuthority, issuer, StringComparison.OrdinalIgnoreCase))
                {
                    options.BackchannelHttpHandler = new ZitadelDockerInternalHandler(
                        publicAuthority: new Uri(issuer),
                        internalAuthority: new Uri(internalAuthority))
                    {
                        InnerHandler = new HttpClientHandler()
                    };
                }
            });

        services.AddAuthorization();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>();

        return services;
    }

    // Rate-limits write endpoints by (user_id || ip), so a logged-in spammer
    // and an anon spammer both get capped, but legitimate parallel users
    // don't share a bucket. Reads are not rate-limited — they're the hot path
    // and already cache-fronted.
    public static IServiceCollection AddReviewsRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(WriteRateLimitPolicy, http =>
            {
                var sub = http.User.FindFirst("sub")?.Value
                          ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                // Compose the partition key from BOTH dimensions so an attacker
                // can't sidestep by rotating IPs while logged in, or by logging
                // in/out from one IP.
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var key = sub is null ? $"ip:{ip}" : $"user:{sub}|ip:{ip}";

                return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,                      // 30 writes/min/partition
                    Window = TimeSpan.FromMinutes(1),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 0,                        // reject immediately, no queueing
                });
            });
        });
}
