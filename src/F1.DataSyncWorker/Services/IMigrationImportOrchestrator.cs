namespace F1.DataSyncWorker.Services;

public interface IMigrationImportOrchestrator
{
    Task RunOnceAsync(CancellationToken cancellationToken);
}