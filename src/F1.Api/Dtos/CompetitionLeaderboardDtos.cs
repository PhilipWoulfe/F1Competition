namespace F1.Api.Dtos;

public sealed record CompetitionLeaderboardResponseDto(
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
    IReadOnlyList<CompetitionLeaderboardEntryDto> Items);

public sealed record CompetitionLeaderboardEntryDto(
    int Position,
    string ParticipantName,
    int DisplayPoints,
    int ImportedPoints,
    int RecalculatedPoints);