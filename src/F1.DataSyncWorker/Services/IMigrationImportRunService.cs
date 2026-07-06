using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public interface IMigrationImportRunService
{
    Task<MigrationImportRunContext?> TryClaimNextQueuedRunAsync(CancellationToken cancellationToken);
    Task<MigrationImportRunContext> StartRunAsync(string sourceFilePath, bool isDryRun, CancellationToken cancellationToken);
    Task StageRowsAsync(Guid runId, IReadOnlyCollection<StagedImportRow> rows, CancellationToken cancellationToken);
    Task CompleteRunAsync(
        Guid runId,
        int rawRowCount,
        CancellationToken cancellationToken,
        MigrationImportRunCompletionMetadata? metadata = null);
    Task FailRunAsync(
        Guid runId,
        string errorMessage,
        CancellationToken cancellationToken,
        MigrationImportRunCompletionMetadata? metadata = null);
}