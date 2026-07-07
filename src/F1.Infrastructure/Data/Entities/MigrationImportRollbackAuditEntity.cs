namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportRollbackAuditEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
    public int AffectedRaceCount { get; set; }
    public int AffectedSelectionCount { get; set; }
    public int AffectedSelectionPositionCount { get; set; }
    public string Outcome { get; set; } = string.Empty;
}
