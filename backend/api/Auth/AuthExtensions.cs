using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Reviews.Api.Services;

namespace Reviews.Api.Auth;

// DelegatingHandler that overrides the Host header on every request. Used to
// reach ZITADEL via the docker-internal DNS name (`zitadel:8080`) while
// presenting the public hostname (`localhost:8080`) ZITADEL expects to
// match its ExternalDomain. Without this the JWKS fetch 404s.
internal sealed class HostHeaderHandler(string host) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Host = host;
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

        // The issuer in JWTs is whatever ZITADEL has as its external URL
        // (`http://localhost:8080` in dev). When the API runs in compose, it
        // can't actually reach `localhost:8080` because that's the host's
        // perspective — internally the IdP is at `http://zitadel:8080`. So
        // we let the metadata-fetch URL differ from the issuer-claim string
        // we validate against.
        var metadataAddress = config["Auth:MetadataAddress"] ?? config["ZITADEL_METADATA_URL"]
            ?? $"{issuer.TrimEnd('/')}/.well-known/openid-configuration";

        // If MetadataAddress points at an internal hostname different from
        // the public issuer (compose case), swap in an HttpClient that sets
        // the Host header to the public hostname so ZITADEL routes the
        // request correctly. Same trick the BFF uses for its OIDC client.
        var issuerHost = new Uri(issuer).Host;
        var metadataHost = new Uri(metadataAddress).Host;
        var needsHostOverride = !issuerHost.Equals(metadataHost, StringComparison.OrdinalIgnoreCase);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = metadataAddress;
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
                if (needsHostOverride)
                {
                    var hostPort = new Uri(issuer).Authority; // host:port
                    options.BackchannelHttpHandler = new HostHeaderHandler(hostPort)
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
