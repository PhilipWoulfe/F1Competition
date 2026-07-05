namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportRawRowEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RowNumber { get; set; }
    public string SectionType { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public string? ClassificationReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}