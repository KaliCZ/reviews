using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Reviews.Infrastructure;

namespace Reviews.Api.Tests;

// End-to-end (in-process) test that posting an invalid JSON body to
// /api/reviews returns a 400 ProblemDetails whose `errors` object names the
// missing required properties. This is the regression net for the wire-level
// validation behaviour that the dropped StrongTypes-throws unit tests used to
// cover indirectly — here we exercise the real ASP.NET pipeline that the SPA
// hits in production.
public class InvalidJsonIntegrationTest : IClassFixture<ReviewsApiFactory>
{
    private readonly ReviewsApiFactory factory;

    public InvalidJsonIntegrationTest(ReviewsApiFactory factory) => this.factory = factory;

    [Fact]
    public async Task Empty_object_post_returns_400_with_property_errors()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        // Force the test auth scheme so the controller's [Authorize] doesn't
        // short-circuit to 401 before model binding runs.
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(TestAuthHandler.Scheme);

        // `{}` omits every required property — productId, rating, title,
        // body, turnstileToken — so model binding should report multiple
        // missing-required errors.
        var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/reviews", content, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(ct);
        using var problem = JsonDocument.Parse(body);

        // Default ASP.NET 400 ProblemDetails includes an `errors` object
        // keyed by property paths. Assert at least one of the required
        // property names appears (case-insensitive — the framework uses the
        // C# member name with default casing in the key).
        var errorsJson = problem.RootElement.GetProperty("errors").GetRawText();
        Assert.Contains("title", errorsJson, StringComparison.OrdinalIgnoreCase);
    }
}

// Boots the API in-memory with the bare minimum config to make Program.cs's
// startup configuration calls succeed. Real Postgres/Redis/Azurite/Temporal
// connections are not opened — model binding runs entirely before any of
// those would be touched, and the test auth handler bypasses the JwtBearer
// path so ZITADEL discovery isn't reached either.
public sealed class ReviewsApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Skip the migrate-and-seed pass — needs a real DB.
                ["Reviews:AutoApply"] = "false",
                // Stubs for the loud-fail "missing key" guards in
                // AddReviewsAuth; the values are unused because
                // TestAuthHandler replaces the JwtBearer scheme.
                ["Auth:IssuerUrl"] = "https://test.invalid",
                ["Auth:Audience"] = "test-aud",
                ["Auth:RequireHttps"] = "false",
                // Loud-fail TurnstileOptions binding wants a key.
                ["Turnstile:SecretKey"] = "stub",
                // Aspire's component registrations expect connection
                // strings; bogus values are fine because we never open them.
                ["ConnectionStrings:reviews"] = "Host=localhost;Database=stub;Username=stub;Password=stub",
                ["ConnectionStrings:cache"] = "localhost:6379",
                ["ConnectionStrings:images"] = "UseDevelopmentStorage=true",
                ["ConnectionStrings:temporal"] = "localhost:7233",
            });
        });
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Pin the default scheme to the test handler — replaces JwtBearer
            // for the duration of the test so [Authorize] gets a synthetic
            // identity instead of trying to reach ZITADEL.
            services.AddAuthentication(TestAuthHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                o.DefaultChallengeScheme = TestAuthHandler.Scheme;
            });

            // Strip every Aspire/EF descriptor pinned to ReviewsDbContext
            // (the pooled options + pool + lease) and replace with a plain
            // in-memory DbContext. The pooled registration's singleton-on-
            // scoped options graph rejects a vanilla AddDbContext otherwise.
            // The controller pre-checks a row before model binding runs, but
            // model binding fails first for the empty-payload case so the
            // DbContext is never resolved — the in-memory provider just
            // satisfies DI graph validation.
            var dbDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("ReviewsDbContext") == true
                         || (d.ServiceType.IsGenericType
                             && d.ServiceType.GetGenericArguments().Any(t => t == typeof(ReviewsDbContext))))
                .ToList();
            foreach (var d in dbDescriptors) services.Remove(d);
            services.AddDbContext<ReviewsDbContext>(opts =>
                opts.UseInMemoryDatabase($"reviews-{Guid.NewGuid():N}"));
        });
    }
}
