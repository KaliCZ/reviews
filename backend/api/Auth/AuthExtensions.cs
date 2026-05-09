using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Reviews.Api.Configuration;
using Reviews.Api.Services;

namespace Reviews.Api.Auth;

// ZITADEL's discovery doc embeds the public host (e.g. `localhost:8080`) in
// jwks_uri / token_endpoint / etc, but inside the container `localhost`
// resolves to the API itself. We rewrite the URL to the docker-internal
// authority and set the Host header back to the public one so ZITADEL's
// vhost routing (ZITADEL_EXTERNALDOMAIN) still matches.
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

    public static IServiceCollection AddReviewsAuth(this IServiceCollection services, IConfiguration config)
    {
        var issuerUrl = config.GetRequired<string>("Auth:IssuerUrl");
        var audience = config.GetRequired<string>("Auth:Audience");
        var requireHttps = config.GetRequired<bool>("Auth:RequireHttps");
        var issuerReachableUrl = config["Auth:IssuerReachableUrl"];

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuerUrl;
                options.Audience = audience;
                options.RequireHttpsMetadata = requireHttps;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuerUrl,
                    ValidateAudience = true,
                    ValidAudience = audience,
                    ValidateLifetime = true,
                    NameClaimType = "name"
                };
                if (!string.IsNullOrEmpty(issuerReachableUrl)
                    && !string.Equals(issuerReachableUrl, issuerUrl, StringComparison.OrdinalIgnoreCase))
                {
                    options.BackchannelHttpHandler = new ZitadelDockerInternalHandler(
                        publicAuthority: new Uri(issuerUrl),
                        internalAuthority: new Uri(issuerReachableUrl))
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

    // Per-user (30/min) AND per-IP (60/min). Anonymous writers skip the user bucket.
    public static IServiceCollection AddReviewsRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(WriteRateLimitPolicy, http =>
            {
                var sub = http.User.FindFirst("sub")?.Value
                          ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Partition key is per (user, ip) but DualWindowLimiter pulls
                // its underlying buckets from process-wide caches keyed by
                // user OR ip alone, so the per-user/per-IP windows stay
                // shared across IP rotation and account fanout.
                var key = sub is null ? $"anon|{ip}" : $"{sub}|{ip}";
                return RateLimitPartition.Get<string>(key, _ => DualWindowLimiter.For(sub, ip));
            });
        });

}

// AND-composes a per-user and a per-IP fixed-window limiter. If one bucket
// rejects after the other granted we dispose the granted lease so the
// rejected request doesn't burn a permit.
internal sealed class DualWindowLimiter : RateLimiter
{
    private const int PerUserPermits = 30;
    private const int PerIpPermits = 60;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static readonly ConcurrentDictionary<string, FixedWindowRateLimiter> UserBuckets = new ConcurrentDictionary<string, FixedWindowRateLimiter>();
    private static readonly ConcurrentDictionary<string, FixedWindowRateLimiter> IpBuckets = new ConcurrentDictionary<string, FixedWindowRateLimiter>();

    private readonly FixedWindowRateLimiter? userBucket;
    private readonly FixedWindowRateLimiter ipBucket;

    private DualWindowLimiter(FixedWindowRateLimiter? userBucket, FixedWindowRateLimiter ipBucket)
    {
        this.userBucket = userBucket;
        this.ipBucket = ipBucket;
    }

    public static DualWindowLimiter For(string? sub, string ip)
    {
        var user = sub is null ? null : UserBuckets.GetOrAdd(sub, _ => new FixedWindowRateLimiter(NewWindowOptions(PerUserPermits)));
        var ipB = IpBuckets.GetOrAdd(ip, _ => new FixedWindowRateLimiter(NewWindowOptions(PerIpPermits)));
        return new DualWindowLimiter(user, ipB);
    }

    private static FixedWindowRateLimiterOptions NewWindowOptions(int permits) => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permits,
        Window = Window,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics? GetStatistics() => null;

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        var ipLease = ipBucket.AttemptAcquire(permitCount);
        if (!ipLease.IsAcquired) return ipLease;

        if (userBucket is null) return ipLease;
        var userLease = userBucket.AttemptAcquire(permitCount);
        if (!userLease.IsAcquired)
        {
            ipLease.Dispose();
            return userLease;
        }

        return new BothLease(ipLease, userLease);
    }

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        var ipLease = await ipBucket.AcquireAsync(permitCount, cancellationToken);
        if (!ipLease.IsAcquired) return ipLease;

        if (userBucket is null) return ipLease;
        var userLease = await userBucket.AcquireAsync(permitCount, cancellationToken);
        if (!userLease.IsAcquired)
        {
            ipLease.Dispose();
            return userLease;
        }

        return new BothLease(ipLease, userLease);
    }

    private sealed class BothLease(RateLimitLease ip, RateLimitLease user) : RateLimitLease
    {
        public override bool IsAcquired => true;
        public override IEnumerable<string> MetadataNames => Array.Empty<string>();
        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            ip.Dispose();
            user.Dispose();
        }
    }
}
