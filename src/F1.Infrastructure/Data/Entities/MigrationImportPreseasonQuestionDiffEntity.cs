namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportPreseasonQuestionDiffEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RowNumber { get; set; }
    public string QuestionKey { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int? ImportedPoints { get; set; }
    public int? CalculatedPoints { get; set; }
    public int DeltaPoints { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
}
