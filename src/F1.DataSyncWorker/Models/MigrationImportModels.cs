namespace F1.DataSyncWorker.Models;

public sealed record StagedImportRow(int RowNumber, string SectionType, string RawPayload, string? ClassificationReason = null);

public sealed record MigrationImportRunContext(
    Guid RunId,
    string SourceFilePath,
    string SourceFileChecksum,
    bool IsDryRun,
    bool PersistDomainEntities);

public sealed record MigrationRaceSelectionParseResult(
    int SelectionCount,
    int UnresolvedTokenCount,
    int PreseasonAnswerCount = 0);

public sealed record MigrationScoreRecalculationResult(
    int ScoredPickCount,
    int TotalPoints,
    int PreseasonScoredQuestionCount = 0,
    int PreseasonTotalPoints = 0,
    int PreseasonScoringWarningCount = 0);

public sealed record MigrationLegacyScoreImportResult(
    int LegacyPickScoreCount,
    int ImportedTotalCount,
    int CalculatedTotalCount);

public sealed record MigrationReconciliationResult(
    int PickDiffCount,
    int RaceDiffCount,
    int ParticipantSummaryCount,
    int ReasonSummaryCount,
    int TotalDelta,
    int PreseasonQuestionDiffCount = 0,
    int PreseasonParticipantSummaryCount = 0,
    int PreseasonReasonSummaryCount = 0,
    int PreseasonTotalDelta = 0);

public sealed record MigrationImportRunCompletionMetadata(
    int UnresolvedTokenCount = 0,
    int MappingWarningCount = 0,
    string PreseasonParseStatus = "NotDetected",
    string PreseasonScoringStatus = "NotDetected",
    int PreseasonWarningCount = 0,
    int PreseasonErrorCount = 0,
    int PreseasonAnswerCount = 0,
    int PreseasonScoredQuestionCount = 0,
    int PreseasonQuestionDiffCount = 0,
    int PreseasonTotalDeltaPoints = 0,
    bool PreseasonIsolationGuardPassed = true,
    string? ParitySnapshotChecksum = null,
    string ParityStatus = "NotCompared",
    string? ParityComparedChecksum = null,
    Guid? ParityComparedRunId = null,
    string? IdempotencyScopeKey = null,
    string IdempotencyOutcome = "Unknown");