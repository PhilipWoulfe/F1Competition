using System.ComponentModel.DataAnnotations;

namespace F1.DataSyncWorker.Options;

public sealed class MigrationExpectedVarianceOptions
{
    public const string SectionName = "MigrationExpectedVariance";

    public bool Enabled { get; set; } = true;

    [Required]
    public string RuleManifestPath { get; set; } = "data/imports/phil-2025/expected-variance-rules.json";
}
