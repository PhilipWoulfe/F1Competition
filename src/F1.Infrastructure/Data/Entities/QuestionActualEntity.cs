namespace F1.Infrastructure.Data.Entities;

public sealed class QuestionActualEntity
{
    public long Id { get; set; }
    public long QuestionTemplateId { get; set; }
    public string? ImportedAnswer { get; set; }
    public string? OverrideAnswer { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}