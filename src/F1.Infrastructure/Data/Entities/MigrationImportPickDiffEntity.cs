namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportPickDiffEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string RaceCode { get; set; } = string.Empty;
    public string PickType { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int? ImportedPoints { get; set; }
    public int? CalculatedPoints { get; set; }
    public int DeltaPoints { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}