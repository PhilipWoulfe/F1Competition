namespace F1.Web.Configuration;

public static class SelectionDefaults
{
    public const string DefaultCompetitionSlug = "main";
    public const int DefaultSeason = 2026;
    public const int DefaultRound = 1;

    // Temporary compatibility values. Remove in PR F cleanup.
    public const string CompatibilityRaceId = "2025-24-yas_marina";
    public const string CompatibilityRoutePath = "yas-marina-selection";
}