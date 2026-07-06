namespace F1.DataSyncWorker.Services;

public sealed class MigrationExpectedVarianceRuleCatalog : IMigrationExpectedVarianceRuleCatalog, IMigrationExpectedVarianceRuleSetMetadataProvider
{
    public static readonly MigrationExpectedVarianceRuleCatalog Empty = new(
        rules: [],
        isEnabled: false,
        ruleSetId: "none",
        ruleSetVersion: "none",
        ruleSetChecksum: "none",
        ruleSource: "none",
        activeEnvironment: "unknown");

    public MigrationExpectedVarianceRuleCatalog(
        IReadOnlyList<MigrationExpectedVarianceRule> rules,
        bool isEnabled = true,
        string ruleSetId = "inline",
        string ruleSetVersion = "unversioned",
        string ruleSetChecksum = "untracked",
        string ruleSource = "inline",
        string activeEnvironment = "unknown")
    {
        Rules = rules;
        IsEnabled = isEnabled;
        RuleSetId = ruleSetId;
        RuleSetVersion = ruleSetVersion;
        RuleSetChecksum = ruleSetChecksum;
        RuleSource = ruleSource;
        ActiveEnvironment = activeEnvironment;
    }

    public IReadOnlyList<MigrationExpectedVarianceRule> Rules { get; }
    public bool IsEnabled { get; }
    public string RuleSetId { get; }
    public string RuleSetVersion { get; }
    public string RuleSetChecksum { get; }
    public string RuleSource { get; }
    public string ActiveEnvironment { get; }
    public int ActiveRuleCount => Rules.Count;
}