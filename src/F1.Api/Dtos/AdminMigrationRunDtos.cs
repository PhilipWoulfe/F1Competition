namespace F1.Api.Dtos;

using Microsoft.AspNetCore.Http;

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
    int UnexpectedTotalDeltaPoints,
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
    int UnexpectedTotalDeltaPoints,
    IReadOnlyList<AdminMigrationUnresolvedTokenSummaryDto> UnresolvedTokenSummary,
    IReadOnlyList<AdminMigrationParticipantDeltaDto> ParticipantDeltas,
    AdminMigrationPreseasonSummaryDto PreseasonSummary,
    IReadOnlyList<AdminMigrationPreseasonParticipantDeltaDto> PreseasonParticipantDeltas,
    IReadOnlyList<AdminMigrationPreseasonQuestionDiffDto> PreseasonQuestionDiffs,
    IReadOnlyList<AdminMigrationPreseasonReasonCategorySummaryDto> PreseasonReasonCategorySummaries,
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

public sealed record AdminMigrationPreseasonSummaryDto(
    int QuestionDiffCount,
    int ParticipantDeltaCount,
    int ReasonCategoryCount,
    int TotalDeltaPoints);

public sealed record AdminMigrationPreseasonParticipantDeltaDto(
    string Subject,
    int ImportedTotalPoints,
    int CalculatedTotalPoints,
    int NetDeltaPoints,
    string? TopReasonCode,
    int TopReasonCount);

public sealed record AdminMigrationPreseasonQuestionDiffDto(
    int RowNumber,
    string QuestionKey,
    string QuestionText,
    string Subject,
    int? ImportedPoints,
    int? CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation);

public sealed record AdminMigrationPreseasonReasonCategorySummaryDto(
    string ReasonCode,
    int OccurrenceCount,
    int TotalDeltaPoints);

public sealed record AdminMigrationRaceDiffDto(
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

public sealed record AdminMigrationPickDiffDto(
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

public sealed record AdminMigrationRunKickoffRequestDto(
    string? SourceFilePath,
    string Mode,
    bool ConfirmNonEmptyStrategy = false);

public sealed record AdminMigrationRunKickoffUploadRequestDto(
    IFormFile? SourceFile,
    string Mode,
    bool ConfirmNonEmptyStrategy = false);

public sealed record AdminMigrationRunKickoffResponseDto(
    Guid RunId,
    string Status,
    bool IsDryRun,
    string RequestedMode,
    string SourceFilePath,
    string SourceFileChecksum,
    DateTime TriggeredAtUtc,
    string RequestedBy,
    string NonEmptyDbStrategy = "merge_upsert_active_records",
    bool CanonicalDataPresent = false,
    int ExistingDriverCount = 0,
    int ExistingRaceCount = 0,
    int ExistingSelectionCount = 0,
    int EstimatedAffectedRaceCount = 0,
    int EstimatedAffectedParticipantCount = 0,
    int EstimatedAffectedSelectionCount = 0);

public sealed record AdminMigrationQuestionDiffListResponseDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<AdminMigrationQuestionDiffDto> Items);

public sealed record AdminMigrationQuestionDiffDto(
    string Category,
    string QuestionId,
    string QuestionText,
    string Participant,
    int? ImportedPoints,
    int CalculatedPoints,
    int DeltaPoints,
    string ReasonCode);

public sealed record AdminMigrationQuestionDiffSummaryResponseDto(
    int TotalCount,
    int NonZeroDeltaCount,
    int TotalDeltaPoints,
    IReadOnlyList<AdminMigrationQuestionDiffCategorySummaryDto> Categories);

public sealed record AdminMigrationQuestionDiffCategorySummaryDto(
    string Category,
    int Count,
    int TotalDeltaPoints);