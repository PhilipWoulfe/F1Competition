namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportRaceSelectionEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RowNumber { get; set; }
    public string RaceCode { get; set; } = string.Empty;
    public string PickType { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? RawValue { get; set; }
    public string? NormalizedValue { get; set; }
    public bool IsActualOutcome { get; set; }
}