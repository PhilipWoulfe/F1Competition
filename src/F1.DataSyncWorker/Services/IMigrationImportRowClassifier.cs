using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public interface IMigrationImportRowClassifier
{
    StagedImportRow Classify(int rowNumber, string rawLine);
}