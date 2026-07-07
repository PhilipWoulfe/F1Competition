namespace F1.Core.Models;

public sealed class QuestionScore
{
    public long Id { get; set; }

    public long QuestionTemplateId { get; set; }

    public string ParticipantId { get; set; } = string.Empty;

    public int? ImportedPoints { get; set; }

    public int CalculatedPoints { get; set; }

    public int DeltaPoints { get; set; }

    public DateTime RecordedAtUtc { get; set; }
}