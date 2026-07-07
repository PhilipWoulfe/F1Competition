namespace F1.Infrastructure.Data.Entities;

public sealed class QuestionScoreEntity
{
    public long Id { get; set; }
    public Guid ImportRunId { get; set; }
    public long QuestionTemplateId { get; set; }
    public string ParticipantId { get; set; } = string.Empty;
    public int? ImportedPoints { get; set; }
    public int CalculatedPoints { get; set; }
    public int DeltaPoints { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; }
}