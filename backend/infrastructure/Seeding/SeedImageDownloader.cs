namespace Reviews.Infrastructure.Seeding;

// Typed HttpClient wrapper for the seed-time picsum downloader. Lets us
// register the dependency by type (`AddHttpClient<SeedImageDownloader>`)
// rather than a magic name string the seeder has to know.
public sealed class SeedImageDownloader(HttpClient http)
{
    public Task<HttpResponseMessage> GetAsync(string seed, CancellationToken ct) =>
        http.GetAsync($"https://picsum.photos/seed/{seed}/800/500", ct);
}
