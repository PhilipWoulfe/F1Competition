using System.ComponentModel.DataAnnotations;

namespace F1.DataSyncWorker.Options;

public sealed class MigrationImportOptions
{
    public const string SectionName = "MigrationImport";

    public bool Enabled { get; set; } = false;

    [Range(1900, 3000)]
    public int Season { get; set; } = 2025;

    [Required]
    public string SourceFilePath { get; set; } = "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv";

    public bool DryRun { get; set; } = true;
}