namespace F1.Core.Models;

public sealed class RaceBonusQuestionTemplateOptions
{
    public string Mode { get; set; } = "Exact";

    public int PointsForCorrectPick { get; set; }

    public decimal? Tolerance { get; set; }

    public decimal? LowerTolerance { get; set; }

    public decimal? UpperTolerance { get; set; }

    public decimal? FormulaMaxPoints { get; set; }

    public decimal? FormulaPenaltyPerUnit { get; set; }
}