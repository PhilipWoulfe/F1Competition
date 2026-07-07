namespace F1.Core.Models;

public sealed class QuestionTemplate
{
    public long Id { get; set; }

    public int CompetitionId { get; set; }

    public int Season { get; set; }

    public string QuestionId { get; set; } = string.Empty;

    public QuestionCategory Category { get; set; }

    public string Prompt { get; set; } = string.Empty;

    public string? OptionsJson { get; set; }

    public QuestionTemplateStatus Status { get; set; }

    public int SortOrder { get; set; }

    // Draft templates remain editable until a run persists answers against them.
    public DateTime CreatedAtUtc { get; set; }

    // After a run completes, template revisions should create a new versioned template instead of mutating history.
    public DateTime UpdatedAtUtc { get; set; }
}