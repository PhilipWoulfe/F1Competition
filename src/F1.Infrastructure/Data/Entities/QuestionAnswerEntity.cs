namespace F1.Infrastructure.Data.Entities;

public sealed class QuestionAnswerEntity
{
    public long Id { get; set; }
    public long QuestionTemplateId { get; set; }
    public string ParticipantId { get; set; } = string.Empty;
    public string? ImportedAnswer { get; set; }
    public string? OverrideAnswer { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}