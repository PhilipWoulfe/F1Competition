namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportPreseasonCalculatedScoreEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RowNumber { get; set; }
    public string QuestionKey { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? PredictedValue { get; set; }
    public string? ActualValue { get; set; }
    public int Points { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
}
