namespace F1.Infrastructure.Data.Entities;

public sealed class QuestionAnswerEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public long QuestionTemplateId { get; set; }
    public string ParticipantId { get; set; } = string.Empty;
    public string? ImportedAnswer { get; set; }
    public string? NormalizedAnswer { get; set; }
    public bool? NormalizedAnswerBoolean { get; set; }
    public int SourceRow { get; set; }
    public int SourceColumn { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}