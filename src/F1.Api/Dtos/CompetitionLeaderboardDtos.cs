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
    decimal DisplayPoints,
    decimal ImportedPoints,
    decimal RecalculatedPoints);

public sealed record CompetitionParticipantDetailResponseDto(
    string CompetitionSlug,
    int Season,
    string DisplayName,
    string ParticipantName,
    CompetitionParticipantSectionSummaryDto RacePicks,
    CompetitionParticipantSectionSummaryDto Preseason,
    CompetitionParticipantSectionSummaryDto H2h);

public sealed record CompetitionParticipantSectionSummaryDto(
    string Title,
    decimal ImportedTotalPoints,
    decimal RecalculatedTotalPoints,
    IReadOnlyList<CompetitionParticipantDetailItemDto> Items);

public sealed record CompetitionParticipantDetailItemDto(
    string Label,
    string Description,
    decimal? ImportedPoints,
    decimal CalculatedPoints,
    decimal DeltaPoints,
    string? ReasonCode,
    string? Explanation);