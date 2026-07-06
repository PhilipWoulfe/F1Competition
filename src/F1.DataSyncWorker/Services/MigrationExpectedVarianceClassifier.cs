using System.Text.RegularExpressions;

namespace F1.DataSyncWorker.Services;

public sealed class MigrationExpectedVarianceClassifier
{
    private readonly IReadOnlyList<MigrationExpectedVarianceRule> _rules;

    public MigrationExpectedVarianceClassifier(IMigrationExpectedVarianceRuleCatalog ruleCatalog)
    {
        _rules = ruleCatalog.Rules;
    }

    public MigrationExpectedVarianceClassification Classify(MigrationExpectedVarianceContext context)
    {
        var matchingRule = _rules.FirstOrDefault(rule => Matches(rule, context));
        if (matchingRule is null)
        {
            return new MigrationExpectedVarianceClassification(false, null, null);
        }

        return new MigrationExpectedVarianceClassification(true, matchingRule.ReasonCode, matchingRule.RuleId);
    }

    private static bool Matches(MigrationExpectedVarianceRule rule, MigrationExpectedVarianceContext context)
    {
        return MatchesText(rule.Subject, context.Subject)
            && MatchesText(rule.RaceCode, context.RaceCode)
            && MatchesText(rule.PickType, context.PickType)
            && MatchesPattern(rule.ImportedSourcePattern, context.ImportedSourceReference)
            && MatchesPattern(rule.CalculatedSourcePattern, context.CalculatedSourceReference);
    }

    private static bool MatchesText(string? pattern, string actual)
    {
        return string.IsNullOrWhiteSpace(pattern)
            || string.Equals(pattern.Trim(), actual, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesPattern(string? pattern, string actual)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        var escaped = Regex.Escape(pattern.Trim())
            .Replace(@"\*", ".*")
            .Replace(@"\?", ".");

        return Regex.IsMatch(
            actual,
            $"^{escaped}$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}