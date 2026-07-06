namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportPreseasonParticipantDeltaSummaryEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int ImportedTotalPoints { get; set; }
    public int CalculatedTotalPoints { get; set; }
    public int NetDeltaPoints { get; set; }
    public string? TopReasonCode { get; set; }
    public int TopReasonCount { get; set; }
}
