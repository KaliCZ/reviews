namespace Reviews.Api.Configuration;

public static class IConfigurationExtensions
{
    // Reads `key` and parses to T. Throws if the key is missing or null.
    // Use for orchestration-supplied config we never want to default at runtime.
    public static T GetRequired<T>(this IConfiguration config, string key)
    {
        if (config[key] is null)
            throw new InvalidOperationException(
                $"Configuration value '{key}' is required and was not set.");
        return config.GetValue<T>(key)!;
    }
}
