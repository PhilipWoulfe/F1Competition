namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportRunEntity
{
    public Guid Id { get; set; }
    public string SourceFilePath { get; set; } = string.Empty;
    public string SourceFileChecksum { get; set; } = string.Empty;
    public bool IsDryRun { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int RawRowCount { get; set; }
    public string? ErrorMessage { get; set; }
}