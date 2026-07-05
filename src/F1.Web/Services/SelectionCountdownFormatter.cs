using F1.Web.Models;

namespace F1.Web.Services;

public interface ISelectionCountdownFormatter
{
    string FormatCountdown(RaceConfig? raceConfig, DateTime nowUtc);
}

public sealed class SelectionCountdownFormatter : ISelectionCountdownFormatter
{
    public string FormatCountdown(RaceConfig? raceConfig, DateTime nowUtc)
    {
        if (raceConfig is null)
        {
            return string.Empty;
        }

        var nextDeadline = nowUtc < raceConfig.PreQualyDeadlineUtc
            ? raceConfig.PreQualyDeadlineUtc
            : raceConfig.FinalDeadlineUtc;
        var label = nowUtc < raceConfig.PreQualyDeadlineUtc ? "Pre-Qualy lock" : "Final submission";

        if (nowUtc > raceConfig.FinalDeadlineUtc)
        {
            return "All deadlines have passed.";
        }

        var remaining = nextDeadline - nowUtc;
        return $"{label} in {remaining.Days}d {remaining.Hours}h {remaining.Minutes}m {remaining.Seconds}s (UTC).";
    }
}