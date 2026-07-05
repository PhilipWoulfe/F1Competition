using System.ComponentModel.DataAnnotations;

namespace F1.DataSyncWorker.Options;

public sealed class MigrationImportOptions
{
    public const string SectionName = "MigrationImport";

    public bool Enabled { get; set; } = false;

    [Required]
    public string SourceFilePath { get; set; } = "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv";

    public bool DryRun { get; set; } = true;
}