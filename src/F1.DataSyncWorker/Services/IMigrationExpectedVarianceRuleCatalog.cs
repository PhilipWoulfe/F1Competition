namespace F1.DataSyncWorker.Services;

public interface IMigrationExpectedVarianceRuleCatalog
{
    IReadOnlyList<MigrationExpectedVarianceRule> Rules { get; }
}