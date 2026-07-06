namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportPreseasonPolicyEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RowNumber { get; set; }
    public int ColumnIndex { get; set; }
    public string CellReference { get; set; } = string.Empty;
    public string? RawPointsPerQuestion { get; set; }
    public int? PointsPerQuestion { get; set; }
}
