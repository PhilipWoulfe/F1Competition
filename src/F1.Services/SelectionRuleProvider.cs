using F1.Core.Models;

namespace F1.Services;

public interface ISelectionRuleProvider
{
    SelectionRuleSet GetRules(Race race, DateTime nowUtc);
}

public sealed class SelectionRuleProvider : ISelectionRuleProvider
{
    private static readonly SelectionRuleDefinition DefaultDefinition = new(
        new[]
        {
            new SelectionRuleBetOption(BetType.Regular, "Regular"),
            new SelectionRuleBetOption(BetType.PreQualy, "Pre-Qualy"),
            new SelectionRuleBetOption(BetType.AllOrNothing, "All-or-Nothing")
        },
        BetType.PreQualy,
        "Pre-Qualy lock",
        "Final submission",
        "This pre-qualy selection is locked.");

    public SelectionRuleSet GetRules(Race race, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(race);

        var betOptions = DefaultDefinition.BetOptions
            .Select(option => new SelectionRuleBetOption(
                option.BetType,
                option.Label,
                IsAvailable(option.BetType, DefaultDefinition.EarlyLockBetType, race, nowUtc)))
            .ToArray();

        return new SelectionRuleSet(
            betOptions,
            DefaultDefinition.EarlyLockBetType,
            DefaultDefinition.EarlyLockLabel,
            DefaultDefinition.FinalSubmissionLabel,
            BuildLockMessage(race, DefaultDefinition.EarlyLockBetType, betOptions),
            DefaultDefinition.LockedSelectionMessage);
    }

    private static bool IsAvailable(BetType betType, BetType? earlyLockBetType, Race race, DateTime nowUtc)
    {
        return earlyLockBetType != betType || nowUtc <= race.PreQualyDeadlineUtc;
    }

    private static string BuildLockMessage(Race race, BetType? earlyLockBetType, IReadOnlyCollection<SelectionRuleBetOption> betOptions)
    {
        if (earlyLockBetType is null)
        {
            return "Selections remain editable until the final submission deadline.";
        }

        var label = betOptions.FirstOrDefault(option => option.BetType == earlyLockBetType)?.Label ?? earlyLockBetType.Value.ToString();
        var lockTimeText = race.PreQualyDeadlineUtc
            .ToString("dd MMM yyyy HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);

        return $"Locking for {label} gives +50% points and prevents changes after {lockTimeText}.";
    }
}

public sealed class SelectionRuleSet(
    IReadOnlyList<SelectionRuleBetOption> betOptions,
    BetType? earlyLockBetType,
    string earlyLockLabel,
    string finalSubmissionLabel,
    string lockMessage,
    string lockedSelectionMessage)
{
    public IReadOnlyList<SelectionRuleBetOption> BetOptions { get; } = betOptions;
    public BetType? EarlyLockBetType { get; } = earlyLockBetType;
    public string EarlyLockLabel { get; } = earlyLockLabel;
    public string FinalSubmissionLabel { get; } = finalSubmissionLabel;
    public string LockMessage { get; } = lockMessage;
    public string LockedSelectionMessage { get; } = lockedSelectionMessage;

    public bool Supports(BetType betType)
    {
        return BetOptions.Any(option => option.BetType == betType);
    }

    public bool IsAvailable(BetType betType)
    {
        return BetOptions.Any(option => option.BetType == betType && option.IsAvailable);
    }

    public string GetLabel(BetType betType)
    {
        return BetOptions.FirstOrDefault(option => option.BetType == betType)?.Label ?? betType.ToString();
    }

    public bool LocksAtEarlyDeadline(BetType betType)
    {
        return EarlyLockBetType == betType;
    }
}

public sealed class SelectionRuleBetOption(BetType betType, string label, bool isAvailable = true)
{
    public BetType BetType { get; } = betType;
    public string Label { get; } = label;
    public bool IsAvailable { get; } = isAvailable;
}

internal sealed class SelectionRuleDefinition(
    IReadOnlyList<SelectionRuleBetOption> betOptions,
    BetType? earlyLockBetType,
    string earlyLockLabel,
    string finalSubmissionLabel,
    string lockedSelectionMessage)
{
    public IReadOnlyList<SelectionRuleBetOption> BetOptions { get; } = betOptions;
    public BetType? EarlyLockBetType { get; } = earlyLockBetType;
    public string EarlyLockLabel { get; } = earlyLockLabel;
    public string FinalSubmissionLabel { get; } = finalSubmissionLabel;
    public string LockedSelectionMessage { get; } = lockedSelectionMessage;
}