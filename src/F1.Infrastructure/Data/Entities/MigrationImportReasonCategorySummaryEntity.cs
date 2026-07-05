namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportReasonCategorySummaryEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public int OccurrenceCount { get; set; }
    public int TotalDeltaPoints { get; set; }
}