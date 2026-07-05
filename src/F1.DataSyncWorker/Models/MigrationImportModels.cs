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
    int UnresolvedTokenCount);

public sealed record MigrationScoreRecalculationResult(
    int ScoredPickCount,
    int TotalPoints);

public sealed record MigrationLegacyScoreImportResult(
    int LegacyPickScoreCount,
    int ImportedTotalCount,
    int CalculatedTotalCount);