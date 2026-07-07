using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public interface IMigrationRaceSelectionParser
{
    Task<MigrationRaceSelectionParseResult> ParseAndPersistAsync(Guid runId, CancellationToken cancellationToken);
}