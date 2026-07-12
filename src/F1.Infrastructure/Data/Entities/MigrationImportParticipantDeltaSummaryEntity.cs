namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportParticipantDeltaSummaryEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int ImportedTotalPoints { get; set; }
    public decimal CalculatedTotalPoints { get; set; }
    public decimal NetDeltaPoints { get; set; }
    public string? TopReasonCode { get; set; }
    public int TopReasonCount { get; set; }
}