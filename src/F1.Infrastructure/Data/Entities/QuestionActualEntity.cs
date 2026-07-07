namespace F1.Infrastructure.Data.Entities;

public sealed class QuestionActualEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public long QuestionTemplateId { get; set; }
    public string? ActualAnswer { get; set; }
    public string? NormalizedAnswer { get; set; }
    public int SourceRow { get; set; }
    public int SourceColumn { get; set; }
    public string? NormalizationDiagnosticsJson { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}