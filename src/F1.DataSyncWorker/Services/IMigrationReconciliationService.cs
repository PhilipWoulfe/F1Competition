using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public interface IMigrationReconciliationService
{
    Task<MigrationReconciliationResult> ReconcileAndPersistAsync(Guid runId, CancellationToken cancellationToken);
}
