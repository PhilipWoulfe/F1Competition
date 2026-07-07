namespace F1.DataSyncWorker.Services;

public interface IDataSyncOrchestrator
{
    Task RunOnceAsync(CancellationToken cancellationToken);
}