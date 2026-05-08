namespace Reviews.Api.Auth;

// Loads a KEY=VALUE dotenv-style file into the process environment so that
// IConfiguration's environment-variable provider picks them up. Used to
// hydrate ZITADEL_* values from /run/secrets/zitadel.env, which the
// zitadel-bootstrap init container generates at runtime (and therefore can't
// be referenced via docker compose `env_file:` at compose-up time).
//
// Existing process env vars win — if you set ZITADEL_ISSUER directly, this
// won't clobber it. No support for quoting, multiline values, or expansion;
// the bootstrap output is plain KEY=VALUE and that's all we need to handle.
public static class DotEnvLoader
{
    // Tries an explicit ZITADEL_ENV_FILE path first (set by Aspire to point
    // at the host bind-mount directory), then falls back to the compose
    // location at /run/secrets/zitadel.env. First file that exists wins;
    // anything else is silently ignored.
    public static void LoadDefaults()
    {
        var explicitPath = Environment.GetEnvironmentVariable("ZITADEL_ENV_FILE");
        if (!string.IsNullOrEmpty(explicitPath)) LoadIfPresent(explicitPath);
        LoadIfPresent("/run/secrets/zitadel.env");
    }

    public static void LoadIfPresent(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var idx = trimmed.IndexOf('=');
            if (idx < 0) continue;
            var key = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim();
            if (Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
