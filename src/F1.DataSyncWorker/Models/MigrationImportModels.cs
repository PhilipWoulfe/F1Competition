namespace F1.DataSyncWorker.Models;

public sealed record StagedImportRow(int RowNumber, string SectionType, string RawPayload);

public sealed record MigrationImportRunContext(
    Guid RunId,
    string SourceFilePath,
    string SourceFileChecksum,
    bool IsDryRun,
    bool PersistDomainEntities);