namespace F1.Core.Models;

public sealed class QuestionAnswer
{
    public long Id { get; set; }

    public Guid ImportRunId { get; set; }

    public long QuestionTemplateId { get; set; }

    public string ParticipantId { get; set; } = string.Empty;

    public string? ImportedAnswer { get; set; }

    public string? NormalizedAnswer { get; set; }

    public int SourceRow { get; set; }

    public int SourceColumn { get; set; }

    // Persisted answer rows become immutable once the owning run completes.
    public DateTime RecordedAtUtc { get; set; }
}