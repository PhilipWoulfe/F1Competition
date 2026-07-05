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
        var required = ParseBool(Environment.GetEnvironmentVariable("E2E_REQUIRED"), false);
        var baseUrl = Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            if (required)
            {
                throw new InvalidOperationException("E2E_BASE_URL environment variable is required when E2E_REQUIRED=true.");
            }

            return new E2eOptions { Enabled = false };
        }

        var apiBaseUrl = Environment.GetEnvironmentVariable("E2E_API_BASE_URL");
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            apiBaseUrl = BuildDefaultApiBaseUrl(baseUrl);
        }

        var timeoutSeconds = ParseInt(Environment.GetEnvironmentVariable("E2E_TIMEOUT_SECONDS"), 20);
        var raceId = Environment.GetEnvironmentVariable("E2E_RACE_ID");
        if (string.IsNullOrWhiteSpace(raceId))
        {
            raceId = "2025-24-yas_marina";
        }
        var headless = ParseBool(Environment.GetEnvironmentVariable("E2E_HEADLESS"), true);

        return new E2eOptions
        {
            Enabled = true,
            BaseUrl = baseUrl.TrimEnd('/'),
            ApiBaseUrl = apiBaseUrl.TrimEnd('/'),
            RaceId = raceId,
            Headless = headless,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            CfClientId = Environment.GetEnvironmentVariable("E2E_CF_CLIENT_ID"),
            CfClientSecret = Environment.GetEnvironmentVariable("E2E_CF_CLIENT_SECRET")
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
}
