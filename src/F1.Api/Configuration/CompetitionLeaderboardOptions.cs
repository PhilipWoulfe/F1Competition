namespace F1.Api.Configuration;

public sealed class CompetitionLeaderboardOptions
{
    public const string SectionName = "CompetitionLeaderboard";

    public List<CompetitionLeaderboardContextOption> Contexts { get; set; } = [];
}

public sealed class CompetitionLeaderboardContextOption
{
    public string CompetitionSlug { get; set; } = string.Empty;

    public int Season { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string SourceType { get; set; } = "Unavailable";

    public string ActiveScoreSource { get; set; } = "ImportedLegacy";

    public string? MigrationSourcePathContains { get; set; }

    public string? UnavailableMessage { get; set; }
}