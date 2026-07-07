namespace F1.DataSyncWorker.Services;

public interface IMigrationRaceRoundMapper
{
    Task<(int SnapshotCount, int MappingCount, int WarningCount)> MapAndPersistAsync(Guid runId, CancellationToken cancellationToken);
}