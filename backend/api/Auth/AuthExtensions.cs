using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Reviews.Api.Configuration;
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
    // Single attribute the controllers tag onto write endpoints. The framework
    // ties it to the policy registered under the same name in
    // AddReviewsRateLimiting.
    public const string WriteRateLimitPolicy = "write";

    // JwtBearer against ZITADEL. Configuration keys:
    //   Auth:IssuerUrl          — public OIDC issuer (the one in JWTs).
    //   Auth:IssuerReachableUrl — issuer URL reachable from inside the
    //                             container; defaults to IssuerUrl. When the
    //                             two differ the backchannel handler kicks in.
    //   Auth:Audience           — OIDC client_id this API expects.
    //   Auth:RequireHttps       — discovery-over-HTTPS gate (true in prod).
    //
    // Owned by us — set by appsettings, AppHost, or docker-compose. None get
    // defaulted at runtime: a misconfigured env should fail loudly at
    // startup, not silently in some code branch.
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

    // Two independent fixed-window buckets ANDed via DualWindowLimiter:
    // every write request must fit BOTH the per-user window (30/min) and the
    // per-IP window (60/min). Per-user catches the logged-in spammer
    // regardless of IP rotation; per-IP catches the multi-account attacker
    // fanning out from one address.
    //
    // Anonymous writers (rare; /api/reviews requires auth) skip the per-user
    // bucket and only see the IP one.
    public static IServiceCollection AddReviewsRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(WriteRateLimitPolicy, http =>
            {
                var sub = http.User.FindFirst("sub")?.Value
                          ?? http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                // Compose a partition key so the framework hands every (user,
                // ip) tuple the same shared limiter — but the limiter itself
                // looks up its underlying buckets from process-wide caches
                // keyed only by user OR only by IP, so a user spanning many
                // IPs still shares the user bucket and an IP spanning many
                // users still shares the IP bucket.
                var key = sub is null ? $"anon|{ip}" : $"{sub}|{ip}";
                return RateLimitPartition.Get<string>(key, _ => DualWindowLimiter.For(sub, ip));
            });
        });

}

// AND-composes a per-user and a per-IP fixed-window limiter for a single
// request. The two underlying FixedWindowRateLimiter instances live in
// process-wide caches keyed by user-sub and IP respectively, so every
// request from the same user shares the same per-user bucket regardless of
// IP — and same for IP across users.
//
// On TryAcquire: ask both, succeed only if both succeed; if one rejects after
// the other granted, dispose the granted lease (release the permit) so the
// uncombined permit isn't accidentally consumed.
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
            // IP grant rolled back so it doesn't consume a permit the user
            // bucket already denied.
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
