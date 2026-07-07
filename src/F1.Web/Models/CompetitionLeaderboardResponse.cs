namespace F1.Web.Models;

public sealed record CompetitionLeaderboardResponse(
    string CompetitionSlug,
    int Season,
    string DisplayName,
    string ActiveScoreSource,
    string ScoreView,
    string ScoreSourceLabel,
    string ScoreSourceHelperText,
    bool IsComparisonAvailable,
    bool IsDataAvailable,
    string? EmptyStateMessage,
    Guid? SourceRunId,
    IReadOnlyList<CompetitionLeaderboardEntry> Items);

public sealed record CompetitionLeaderboardEntry(
    int Position,
    string ParticipantName,
    int DisplayPoints,
    int ImportedPoints,
    int RecalculatedPoints);