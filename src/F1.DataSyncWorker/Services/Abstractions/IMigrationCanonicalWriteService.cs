namespace F1.DataSyncWorker.Services;

public interface IMigrationCanonicalWriteService
{
    Task PersistCanonicalEntitiesAsync(Guid runId, CancellationToken cancellationToken);
}
