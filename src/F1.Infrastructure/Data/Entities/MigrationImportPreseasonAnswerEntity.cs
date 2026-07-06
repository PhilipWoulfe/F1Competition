namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportPreseasonAnswerEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RowNumber { get; set; }
    public string QuestionKey { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? RawAnswer { get; set; }
    public string? NormalizedAnswer { get; set; }
    public bool IsActualOutcome { get; set; }
}