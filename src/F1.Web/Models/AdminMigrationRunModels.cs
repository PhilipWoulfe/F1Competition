namespace F1.Web.Models;

public sealed record AdminMigrationRunListResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<AdminMigrationRunListItem> Items);

public sealed record AdminMigrationRunListItem(
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

public sealed record AdminMigrationRunDetailResponse(
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
    IReadOnlyList<AdminMigrationUnresolvedTokenSummary> UnresolvedTokenSummary,
    IReadOnlyList<AdminMigrationParticipantDelta> ParticipantDeltas,
    IReadOnlyList<AdminMigrationRaceDiff> RaceDiffs,
    IReadOnlyList<AdminMigrationPickDiff> PickDiffs);

public sealed record AdminMigrationUnresolvedTokenSummary(
    string RawToken,
    int OccurrenceCount,
    int FirstRowNumber,
    DateTime FirstSeenAtUtc);

public sealed record AdminMigrationParticipantDelta(
    string Subject,
    int ImportedTotalPoints,
    int CalculatedTotalPoints,
    int NetDeltaPoints,
    string? TopReasonCode,
    int TopReasonCount);

public sealed record AdminMigrationRaceDiff(
    string RaceCode,
    string Subject,
    int ImportedPoints,
    int CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation);

public sealed record AdminMigrationPickDiff(
    string RaceCode,
    string PickType,
    string Subject,
    int? ImportedPoints,
    int? CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation);

public sealed record AdminMigrationRunKickoffRequest(
    string? SourceFilePath,
    string Mode);

public sealed record AdminMigrationRunKickoffResponse(
    Guid RunId,
    string Status,
    bool IsDryRun,
    string RequestedMode,
    string SourceFilePath,
    string SourceFileChecksum,
    DateTime TriggeredAtUtc,
    string RequestedBy);
