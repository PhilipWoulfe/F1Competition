namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportRaceDiffEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public string RaceCode { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int ImportedPoints { get; set; }
    public decimal CalculatedPoints { get; set; }
    public decimal DeltaPoints { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public bool IsExpectedVariance { get; set; }
    public string? ExpectedVarianceReasonCode { get; set; }
    public string? ExpectedVarianceRuleId { get; set; }
    public string Explanation { get; set; } = string.Empty;
}