using Npgsql;

namespace F1.E2E.Tests.Infrastructure;

internal class E2eOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
    public string? RaceId { get; init; }
    public string CompetitionSlug { get; init; } = "main";
    public int Season { get; init; } = 2026;
    public int Round { get; init; } = 1;
    public bool Headless { get; init; } = true;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
    public string? CfClientId { get; init; }
    public string? CfClientSecret { get; init; }
    public string? PostgresConnectionString { get; init; }

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
        var competitionSlug = GetSetting("E2E_COMPETITION_SLUG") ?? "main";
        var season = ParseInt(GetSetting("E2E_SEASON"), 2026);
        var round = ParseInt(GetSetting("E2E_ROUND"), 1);
        var headless = ParseBool(GetSetting("E2E_HEADLESS"), true);
        var postgresConnectionString = BuildPostgresConnectionString(GetSetting);

        return new E2eOptions
        {
            Enabled = true,
            BaseUrl = baseUrl.TrimEnd('/'),
            ApiBaseUrl = apiBaseUrl.TrimEnd('/'),
            RaceId = raceId,
            CompetitionSlug = competitionSlug,
            Season = season,
            Round = round,
            Headless = headless,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            CfClientId = GetSetting("E2E_CF_CLIENT_ID"),
            CfClientSecret = GetSetting("E2E_CF_CLIENT_SECRET"),
            PostgresConnectionString = postgresConnectionString
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

    public string BuildSelectionRoutePath()
    {
        if (!string.IsNullOrWhiteSpace(RaceId))
        {
            return $"selection/{RaceId}";
        }

        return $"selection/{CompetitionSlug}/{Season}/round/{Round}";
    }

    private static string? BuildPostgresConnectionString(Func<string, string?> getSetting)
    {
        var explicitConnectionString = getSetting("E2E_POSTGRES_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            return explicitConnectionString;
        }

        var database = getSetting("POSTGRES_DB");
        var username = getSetting("POSTGRES_USER");
        var password = getSetting("POSTGRES_PASSWORD");

        if (string.IsNullOrWhiteSpace(database) ||
            string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var host = getSetting("POSTGRES_HOST");
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "localhost";
        }

        var port = ParseIntAllowZero(getSetting("POSTGRES_PORT"), 5432);
        if (port <= 0)
        {
            port = 5432;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            Password = password
        };
        return builder.ToString();
    }

    private static int ParseIntAllowZero(string? raw, int fallback)
    {
        return int.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static Dictionary<string, string> LoadLocalDotEnv()
    {
        var envPath = Path.Combine(E2ePathResolver.ResolveRepositoryRoot(), ".env");
        if (!File.Exists(envPath))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> rawLines;
        try
        {
            rawLines = File.ReadLines(envPath);
        }
        catch (IOException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var rawLine in rawLines)
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
