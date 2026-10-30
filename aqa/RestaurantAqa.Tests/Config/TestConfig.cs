using System;
using System.IO;

namespace RestaurantAqa.Tests.Config;

/// <summary>
/// Reads test configuration from environment variables, loading a local ".env" file
/// (if present, next to the test project) into the process environment first.
/// Nothing here is a secret - it's just which deployment to point the tests at.
/// </summary>
public static class TestConfig
{
    private static readonly object Lock = new();
    private static bool _loaded;

    public static string FrontendUrl => Get("AQA_FRONTEND_URL", "https://d3huyvh0kh27ah.cloudfront.net");
    public static string ApiBaseUrl => Get("AQA_API_BASE_URL", "https://podlgit99l.execute-api.eu-north-1.amazonaws.com/develop");
    public static string LocationId => Get("AQA_LOCATION_ID", "2de08053-9bec-4662-bf56-2ccb70aeeb78");

    private static string Get(string key, string fallback)
    {
        EnsureEnvLoaded();
        return Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;
    }

    private static void EnsureEnvLoaded()
    {
        if (_loaded) return;
        lock (Lock)
        {
            if (_loaded) return;
            _loaded = true;

            // Walk up from the test run's base directory to find a ".env" file
            // (works whether tests run from bin/Debug/net10.0 or the project root).
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (!File.Exists(candidate)) continue;

                foreach (var rawLine in File.ReadAllLines(candidate))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#')) continue;

                    var separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0) continue;

                    var key = line[..separatorIndex].Trim();
                    var value = line[(separatorIndex + 1)..].Trim().Trim('"');

                    // Don't override a value someone already set explicitly in the environment (e.g. in CI).
                    if (Environment.GetEnvironmentVariable(key) is null)
                        Environment.SetEnvironmentVariable(key, value);
                }
                break;
            }
        }
    }
}
