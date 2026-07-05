using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public interface IMigrationLegacyScoreImporter
{
    Task<MigrationLegacyScoreImportResult> ImportAndPersistAsync(Guid runId, CancellationToken cancellationToken);
}