namespace F1.Infrastructure.Data.Entities;

public sealed class MigrationImportPreseasonImportedTallyEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public int RowNumber { get; set; }
    public string QuestionKey { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? RawPoints { get; set; }
    public int? ImportedPoints { get; set; }
}
