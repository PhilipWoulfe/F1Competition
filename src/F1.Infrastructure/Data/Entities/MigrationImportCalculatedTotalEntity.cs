namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportCalculatedTotalEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public decimal CalculatedTotalPoints { get; set; }
}