namespace Reviews.Api.Configuration;

public static class IConfigurationExtensions
{
    public static T GetRequired<T>(this IConfiguration config, string key)
    {
        if (config[key] is null)
            throw new InvalidOperationException(
                $"Configuration value '{key}' is required and was not set.");
        return config.GetValue<T>(key)!;
    }
}
