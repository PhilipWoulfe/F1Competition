namespace F1.Core.Models;

public sealed class H2hQuestionTemplateOptions
{
    public string LeftDriverId { get; set; } = string.Empty;

    public string RightDriverId { get; set; } = string.Empty;

    public int PointsForCorrectPick { get; set; }
}