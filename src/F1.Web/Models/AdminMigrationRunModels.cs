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
    int UnexpectedTotalDeltaPoints,
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
    int UnexpectedTotalDeltaPoints,
    IReadOnlyList<AdminMigrationUnresolvedTokenSummary> UnresolvedTokenSummary,
    IReadOnlyList<AdminMigrationParticipantDelta> ParticipantDeltas,
    AdminMigrationPreseasonSummary PreseasonSummary,
    IReadOnlyList<AdminMigrationPreseasonParticipantDelta> PreseasonParticipantDeltas,
    IReadOnlyList<AdminMigrationPreseasonQuestionDiff> PreseasonQuestionDiffs,
    IReadOnlyList<AdminMigrationPreseasonReasonCategorySummary> PreseasonReasonCategorySummaries,
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

public sealed record AdminMigrationPreseasonSummary(
    int QuestionDiffCount,
    int ParticipantDeltaCount,
    int ReasonCategoryCount,
    int TotalDeltaPoints);

public sealed record AdminMigrationPreseasonParticipantDelta(
    string Subject,
    int ImportedTotalPoints,
    int CalculatedTotalPoints,
    int NetDeltaPoints,
    string? TopReasonCode,
    int TopReasonCount);

public sealed record AdminMigrationPreseasonQuestionDiff(
    int RowNumber,
    string QuestionKey,
    string QuestionText,
    string Subject,
    int? ImportedPoints,
    int? CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation);

public sealed record AdminMigrationPreseasonReasonCategorySummary(
    string ReasonCode,
    int OccurrenceCount,
    int TotalDeltaPoints);

public sealed record AdminMigrationRaceDiff(
    string RaceCode,
    string Subject,
    int ImportedPoints,
    int CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation,
    bool IsExpectedVariance = false,
    string? ExpectedVarianceReasonCode = null,
    string? ExpectedVarianceRuleId = null);

public sealed record AdminMigrationPickDiff(
    string RaceCode,
    string PickType,
    string Subject,
    int? ImportedPoints,
    int? CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation,
    bool IsExpectedVariance = false,
    string? ExpectedVarianceReasonCode = null,
    string? ExpectedVarianceRuleId = null);

public sealed record AdminMigrationRunKickoffRequest(
    string? SourceFilePath,
    string Mode);

public sealed record AdminMigrationRunKickoffUploadRequest(
    string FileName,
    Stream Content,
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
