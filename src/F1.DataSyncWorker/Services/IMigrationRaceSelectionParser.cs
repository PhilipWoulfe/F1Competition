namespace F1.DataSyncWorker.Services;

public interface IMigrationRaceSelectionParser
{
    Task<int> ParseAndPersistAsync(Guid runId, CancellationToken cancellationToken);
}