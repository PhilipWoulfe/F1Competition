namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportConflictDiagnosticEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string ConflictType { get; set; } = string.Empty;
    public string KeyFields { get; set; } = string.Empty;
    public string SourceReference { get; set; } = string.Empty;
    public string PolicyOutcome { get; set; } = string.Empty;
    public string RecommendedAction { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
