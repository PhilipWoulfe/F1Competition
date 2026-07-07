using F1.Core.Models;

namespace F1.Infrastructure.Data.Entities;

public sealed class QuestionTemplateEntity
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
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}