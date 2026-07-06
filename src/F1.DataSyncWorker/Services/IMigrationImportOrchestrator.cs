namespace F1.DataSyncWorker.Services;

public interface IMigrationImportOrchestrator
{
    Task<bool> RunNextQueuedAsync(CancellationToken cancellationToken);
    Task RunOnceAsync(CancellationToken cancellationToken);
}