namespace F1.E2E.Tests.Infrastructure;

internal class E2eOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string RaceId { get; init; } = "2025-24-yas_marina";
    public bool Headless { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
    public string? CfClientId { get; init; }
    public string? CfClientSecret { get; init; }

    public static E2eOptions FromEnvironment()
    {
        var localSettings = new Lazy<Dictionary<string, string>>(LoadLocalDotEnv);

        string? GetSetting(string name)
        {
            var envValue = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(envValue))
            {
                return envValue;
            }

            return localSettings.Value.TryGetValue(name, out var localValue) && !string.IsNullOrWhiteSpace(localValue)
                ? localValue
                : null;
        }

        var required = ParseBool(GetSetting("E2E_REQUIRED"), false);
        var baseUrl = GetSetting("E2E_BASE_URL") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (required)
            {
                throw new InvalidOperationException("E2E_BASE_URL environment variable is required when E2E_REQUIRED=true.");
            }

            return new E2eOptions { Enabled = false };
        }

        var apiBaseUrl = GetSetting("E2E_API_BASE_URL");
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            apiBaseUrl = BuildDefaultApiBaseUrl(baseUrl);
        }

        var timeoutSeconds = ParseInt(GetSetting("E2E_TIMEOUT_SECONDS"), 20);
        var raceId = GetSetting("E2E_RACE_ID");
        if (string.IsNullOrWhiteSpace(raceId))
        {
            raceId = "2025-24-yas_marina";
        }
        var headless = ParseBool(GetSetting("E2E_HEADLESS"), true);

        return new E2eOptions
        {
            Enabled = true,
            BaseUrl = baseUrl.TrimEnd('/'),
            ApiBaseUrl = apiBaseUrl.TrimEnd('/'),
            RaceId = raceId,
            Headless = headless,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            CfClientId = GetSetting("E2E_CF_CLIENT_ID"),
            CfClientSecret = GetSetting("E2E_CF_CLIENT_SECRET")
        };
    }

    public Dictionary<string, string> BuildCloudflareHeaders()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(CfClientId) && !string.IsNullOrWhiteSpace(CfClientSecret))
        {
            headers["CF-Access-Client-Id"] = CfClientId;
            headers["CF-Access-Client-Secret"] = CfClientSecret;
        }

        return headers;
    }

    private static string BuildDefaultApiBaseUrl(string baseUrl)
    {
        return baseUrl.TrimEnd('/') + "/api";
    }

    private static int ParseInt(string? raw, int fallback)
    {
        return int.TryParse(raw, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool ParseBool(string? raw, bool fallback)
    {
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static Dictionary<string, string> LoadLocalDotEnv()
    {
        var envPath = Path.Combine(E2ePathResolver.ResolveRepositoryRoot(), ".env");
        if (!File.Exists(envPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in File.ReadLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = line[..equalsIndex].Trim();
            var value = line[(equalsIndex + 1)..].Trim();
            if (value.Length >= 2)
            {
                var first = value[0];
                var last = value[^1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                {
                    value = value[1..^1];
                }
            }

            if (key.Length > 0)
            {
                values[key] = value;
            }
        }

        return values;
    }
}
