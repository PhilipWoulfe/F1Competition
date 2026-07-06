namespace F1.DataSyncWorker.Services;

public interface IMigrationExpectedVarianceRuleSetMetadataProvider
{
    bool IsEnabled { get; }
    string RuleSetId { get; }
    string RuleSetVersion { get; }
    string RuleSetChecksum { get; }
    string RuleSource { get; }
    string ActiveEnvironment { get; }
    int ActiveRuleCount { get; }
}
