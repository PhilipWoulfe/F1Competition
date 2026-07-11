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
    IReadOnlyList<AdminMigrationConflictDiagnostic> ConflictDiagnostics,
    IReadOnlyList<AdminMigrationRaceDiff> RaceDiffs,
    IReadOnlyList<AdminMigrationPickDiff> PickDiffs,
    IReadOnlyList<AdminMigrationParticipantComponentDelta>? ParticipantComponentDeltas = null,
    IReadOnlyList<AdminMigrationCdpParity>? CdpParity = null,
    IReadOnlyList<AdminMigrationSourceManifestItem>? SourceManifest = null,
    IReadOnlyList<AdminMigrationSourceContractDiagnostic>? SourceContractDiagnostics = null,
    int? H2hPointsPolicy = null,
    int? PreseasonPointsPolicy = null,
    IReadOnlyList<AdminMigrationRaceBonusMode>? RaceBonusModes = null,
    IReadOnlyList<AdminMigrationRollbackAudit>? RollbackAudits = null);

public sealed record AdminMigrationRaceBonusMode(
    string QuestionId,
    string Prompt,
    string Mode,
    int PointsForCorrectPick,
    decimal? Tolerance,
    decimal? LowerTolerance,
    decimal? UpperTolerance,
    decimal? FormulaMaxPoints,
    decimal? FormulaPenaltyPerUnit);

public sealed record AdminMigrationRollbackAudit(
    DateTime RequestedAtUtc,
    string Actor,
    string Reason,
    string Outcome,
    int AffectedRaceCount,
    int AffectedSelectionCount,
    int AffectedSelectionPositionCount);

public sealed record AdminMigrationConflictDiagnostic(
    string EntityType,
    string ConflictType,
    string KeyFields,
    string SourceReference,
    string PolicyOutcome,
    string RecommendedAction,
    DateTime CreatedAtUtc);

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
    string? ChosenAnswer,
    string? ActualAnswer,
    int? ImportedPoints,
    int? CalculatedPoints,
    int DeltaPoints,
    string ReasonCode,
    string Explanation);

public sealed record AdminMigrationPreseasonReasonCategorySummary(
    string ReasonCode,
    int OccurrenceCount,
    int TotalDeltaPoints);

public sealed record AdminMigrationParticipantComponentDelta(
    string Subject,
    int ImportedRacePoints,
    int CalculatedRacePoints,
    int ImportedPreseasonPoints,
    int CalculatedPreseasonPoints,
    int ImportedTotalPoints,
    int CalculatedTotalPoints,
    int NetDeltaPoints,
    string? TopReasonCode,
    int TopReasonCount);

public sealed record AdminMigrationCdpParity(
    string Subject,
    int? ImportedCdp,
    int CalculatedCdp,
    int Delta,
    bool IsParity);

public sealed record AdminMigrationSourceManifestItem(
    string SourceFileName,
    int RowCount,
    int HeaderCount,
    int RacePickCount,
    int SeasonQuestionPredictionCount,
    int RacePointsCount,
    int TotalsMetaCount,
    int UnclassifiedCount,
    int SourceArtifactCount);

public sealed record AdminMigrationSourceContractDiagnostic(
    string Code,
    string Severity,
    string Message);

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
    string? ChosenAnswer,
    string? ActualAnswer,
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
    string Mode,
    string? SourceProfile = null,
    bool ConfirmNonEmptyStrategy = false);

public sealed record AdminMigrationRunKickoffUploadRequest(
    string FileName,
    Stream Content,
    string Mode,
    string? SourceProfile = null,
    bool ConfirmNonEmptyStrategy = false);

public sealed record AdminMigrationRunKickoffResponse(
    Guid RunId,
    string Status,
    bool IsDryRun,
    string RequestedMode,
    string SourceFilePath,
    string SourceFileChecksum,
    DateTime TriggeredAtUtc,
    string RequestedBy,
    string NonEmptyDbStrategy,
    bool CanonicalDataPresent,
    int ExistingDriverCount,
    int ExistingRaceCount,
    int ExistingSelectionCount,
    int EstimatedAffectedRaceCount,
    int EstimatedAffectedParticipantCount,
    int EstimatedAffectedSelectionCount);

public sealed record AdminMigrationQuestionDiffListResponse(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<AdminMigrationQuestionDiff> Items);

public sealed record AdminMigrationQuestionDiff(
    string Category,
    string QuestionId,
    string QuestionText,
    string Participant,
    string? ChosenAnswer,
    string? ActualAnswer,
    int? ImportedPoints,
    int CalculatedPoints,
    int DeltaPoints,
    string ReasonCode);

public sealed record AdminMigrationQuestionDiffSummaryResponse(
    int TotalCount,
    int NonZeroDeltaCount,
    int TotalDeltaPoints,
    IReadOnlyList<AdminMigrationQuestionDiffCategorySummary> Categories);

public sealed record AdminMigrationQuestionDiffCategorySummary(
    string Category,
    int Count,
    int TotalDeltaPoints);
