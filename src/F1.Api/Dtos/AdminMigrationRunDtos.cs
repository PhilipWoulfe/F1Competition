namespace F1.Api.Dtos;

public sealed record AdminMigrationRunListResponseDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<AdminMigrationRunListItemDto> Items);

public sealed record AdminMigrationRunListItemDto(
    Guid RunId,
    string Status,
    bool IsDryRun,
    string SourceFilePath,
    string SourceFileChecksum,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int RawRowCount,
    int UnresolvedTokenCount,
    int PickDiffCount,
    int RaceDiffCount,
    int TotalDeltaPoints,
    string? ErrorMessage);

public sealed record AdminMigrationRunDetailResponseDto(
    Guid RunId,
    string Status,
    bool IsDryRun,
    string SourceFilePath,
    string SourceFileChecksum,
    DateTime StartedAtUtc,
    DateTime? FinishedAtUtc,
    int RawRowCount,
    string? ErrorMessage,
    int UnresolvedTokenCount,
    int PickDiffCount,
    int RaceDiffCount,
    int TotalDeltaPoints,
    IReadOnlyList<AdminMigrationUnresolvedTokenSummaryDto> UnresolvedTokenSummary,
    IReadOnlyList<AdminMigrationParticipantDeltaDto> ParticipantDeltas,
    IReadOnlyList<AdminMigrationRaceDiffDto> RaceDiffs,
    IReadOnlyList<AdminMigrationPickDiffDto> PickDiffs);

public sealed record AdminMigrationUnresolvedTokenSummaryDto(
    string RawToken,
    int OccurrenceCount,
    int FirstRowNumber,
    DateTime FirstSeenAtUtc);

public sealed record AdminMigrationParticipantDeltaDto(
    string Subject,
    int ImportedTotalPoints,
    int CalculatedTotalPoints,
    int NetDeltaPoints,
    string? TopReasonCode,
    int TopReasonCount);

public sealed record AdminMigrationRaceDiffDto(
    string RaceCode,
    string Subject,
    int ImportedPoints,
    int CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation);

public sealed record AdminMigrationPickDiffDto(
    string RaceCode,
    string PickType,
    string Subject,
    int? ImportedPoints,
    int? CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation);