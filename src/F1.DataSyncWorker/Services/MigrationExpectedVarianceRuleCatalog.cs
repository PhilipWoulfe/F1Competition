namespace F1.DataSyncWorker.Services;

public sealed class MigrationExpectedVarianceRuleCatalog : IMigrationExpectedVarianceRuleCatalog
{
    public static readonly MigrationExpectedVarianceRuleCatalog Empty = new([]);

    public MigrationExpectedVarianceRuleCatalog(IReadOnlyList<MigrationExpectedVarianceRule> rules)
    {
        Rules = rules;
    }

    public IReadOnlyList<MigrationExpectedVarianceRule> Rules { get; }
}