namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportImportedTotalEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string? RawTotal { get; set; }
    public int? ImportedTotalPoints { get; set; }
}