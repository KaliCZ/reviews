namespace Reviews.Infrastructure.Seeding;

public sealed class SeedImageDownloader(HttpClient http)
{
    public Task<HttpResponseMessage> GetAsync(string seed, CancellationToken ct) =>
        http.GetAsync($"https://picsum.photos/seed/{seed}/800/500", ct);
}
