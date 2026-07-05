using F1.Core.Models;

namespace F1.Services;

public interface ICompetitionRuleCatalog
{
    CompetitionRuleDefinition GetForRace(Race race);
}

public sealed class CompetitionRuleCatalog : ICompetitionRuleCatalog
{
    private const string LegacyPhil2025Key = "philip-2025";
    private const string Dave2025Key = "david-2025";
    private const string Main2026Key = "main-2026";

    private static readonly IReadOnlyDictionary<string, CompetitionRuleDefinition> Definitions =
        new Dictionary<string, CompetitionRuleDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            [LegacyPhil2025Key] = new CompetitionRuleDefinition(
                LegacyPhil2025Key,
                3,
                [
                    new SelectionRuleBetOption(BetType.Regular, "Regular"),
                    new SelectionRuleBetOption(BetType.PreQualy, "Pre-Qualy"),
                    new SelectionRuleBetOption(BetType.AllOrNothing, "All-or-Nothing")
                ],
                BetType.PreQualy,
                "Pre-Qualy lock",
                "Final submission",
                "This pre-qualy selection is locked."),
            [Dave2025Key] = new CompetitionRuleDefinition(
                Dave2025Key,
                3,
                [
                    new SelectionRuleBetOption(BetType.Regular, "Regular"),
                    new SelectionRuleBetOption(BetType.PreQualy, "Pre-Qualy"),
                    new SelectionRuleBetOption(BetType.AllOrNothing, "All-or-Nothing")
                ],
                BetType.PreQualy,
                "Pre-Qualy lock",
                "Final submission",
                "This pre-qualy selection is locked."),
            [Main2026Key] = new CompetitionRuleDefinition(
                Main2026Key,
                5,
                [
                    new SelectionRuleBetOption(BetType.Regular, "Regular"),
                    new SelectionRuleBetOption(BetType.PreQualy, "Pre-Qualy"),
                    new SelectionRuleBetOption(BetType.AllOrNothing, "All-or-Nothing")
                ],
                BetType.PreQualy,
                "Pre-Qualy lock",
                "Final submission",
                "This pre-qualy selection is locked.")
        };

    public CompetitionRuleDefinition GetForRace(Race race)
    {
        ArgumentNullException.ThrowIfNull(race);

        var competitionKey = ResolveCompetitionKey(race);
        if (Definitions.TryGetValue(competitionKey, out var definition))
        {
            return definition;
        }

        return race.Season <= 2025 ? Definitions[LegacyPhil2025Key] : Definitions[Main2026Key];
    }

    private static string ResolveCompetitionKey(Race race)
    {
        if (TryExtractCompetitionSlug(race.Id, out var slug))
        {
            return slug;
        }

        if (race.Id.StartsWith("2025-", StringComparison.OrdinalIgnoreCase))
        {
            return LegacyPhil2025Key;
        }

        if (race.Id.StartsWith("2026-", StringComparison.OrdinalIgnoreCase))
        {
            return Main2026Key;
        }

        return race.Season <= 2025 ? LegacyPhil2025Key : Main2026Key;
    }

    private static bool TryExtractCompetitionSlug(string raceId, out string slug)
    {
        slug = string.Empty;
        if (string.IsNullOrWhiteSpace(raceId))
        {
            return false;
        }

        var parts = raceId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 2; i++)
        {
            if (parts[i].Length == 4
                && int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)
                && int.TryParse(parts[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                if (i == 0)
                {
                    return false;
                }

                slug = string.Join('-', parts[..i]);
                return !string.IsNullOrWhiteSpace(slug);
            }
        }

        return false;
    }
}

public sealed record CompetitionRuleDefinition(
    string CompetitionKey,
    int SelectionCount,
    IReadOnlyList<SelectionRuleBetOption> BetOptions,
    BetType? EarlyLockBetType,
    string EarlyLockLabel,
    string FinalSubmissionLabel,
    string LockedSelectionMessage);