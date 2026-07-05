using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public interface IMigrationScoreRecalculator
{
    Task<MigrationScoreRecalculationResult> RecalculateAndPersistAsync(Guid runId, CancellationToken cancellationToken);
}