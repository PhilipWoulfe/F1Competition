namespace F1.Core.Models;

public sealed class QuestionActual
{
    public long Id { get; set; }

    public long QuestionTemplateId { get; set; }

    public string? ImportedAnswer { get; set; }

    public string? OverrideAnswer { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}