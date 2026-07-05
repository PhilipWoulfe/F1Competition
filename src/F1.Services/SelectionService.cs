using F1.Core.Dtos;
using F1.Core.Interfaces;
using F1.Core.Models;

namespace F1.Services;

public class SelectionService : ISelectionService
{
    private readonly ISelectionRepository _selectionRepository;
    private readonly IDriverRepository _driverRepository;
    private readonly IRaceRepository _raceRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ISelectionRuleProvider _selectionRuleProvider;

    public SelectionService(
        ISelectionRepository selectionRepository,
        IDriverRepository driverRepository,
        IRaceRepository raceRepository,
        IDateTimeProvider dateTimeProvider,
        ISelectionRuleProvider selectionRuleProvider)
    {
        _selectionRepository = selectionRepository;
        _driverRepository = driverRepository;
        _raceRepository = raceRepository;
        _dateTimeProvider = dateTimeProvider;
        _selectionRuleProvider = selectionRuleProvider;
    }

    public async Task<Selection?> GetSelectionAsync(string raceId, string userId)
    {
        var selection = await _selectionRepository.GetSelectionAsync(raceId, userId);

        if (selection is null)
        {
            return null;
        }

        var nowUtc = _dateTimeProvider.UtcNow;
        var race = await _raceRepository.GetRaceAsync(selection.RaceId);
        selection.IsLocked = race is null || IsLocked(selection, race, nowUtc);
        return selection;
    }

    public async Task<Selection> UpsertSelectionAsync(string raceId, string userId, SelectionSubmissionDto submission)
    {
        var orderedSelections = submission.OrderedSelections;
        ValidateSelections(orderedSelections);

        var race = await _raceRepository.GetRaceAsync(raceId);
        if (race is null)
        {
            throw new SelectionRaceNotFoundException($"Race '{raceId}' not found.");
        }

        var nowUtc = _dateTimeProvider.UtcNow;
        var rules = _selectionRuleProvider.GetRules(race, nowUtc);
        var existingSelection = await _selectionRepository.GetSelectionAsync(raceId, userId);

        if (!rules.Supports(submission.BetType))
        {
            throw new SelectionValidationException($"{rules.GetLabel(submission.BetType)} is not available for this competition.");
        }

        if (existingSelection is not null && IsLocked(existingSelection, race, nowUtc))
        {
            if (nowUtc > race.FinalDeadlineUtc)
            {
                throw new SelectionForbiddenException($"Selections are locked after {race.FinalDeadlineUtc:u}.");
            }

            var lockedLabel = rules.EarlyLockBetType is null ? "Selections" : $"{rules.GetLabel(rules.EarlyLockBetType.Value)} selections";
            throw new SelectionForbiddenException($"{lockedLabel} cannot be edited after {race.PreQualyDeadlineUtc:u}.");
        }

        if (!rules.IsAvailable(submission.BetType))
        {
            throw new SelectionValidationException($"{rules.GetLabel(submission.BetType)} strategy is no longer available after {race.PreQualyDeadlineUtc:u}.");
        }

        var selection = existingSelection ?? new Selection();
        selection.Id = existingSelection?.Id ?? Guid.Empty;
        selection.RaceId = raceId;
        selection.UserId = userId;
        selection.OrderedSelections = orderedSelections;
        selection.BetType = submission.BetType;
        selection.SubmittedAtUtc = nowUtc;
        selection.IsLocked = false;

        return await _selectionRepository.UpsertSelectionAsync(selection);
    }

    public async Task<IReadOnlyList<CurrentSelectionDto>> GetCurrentSelectionsAsync(string raceId, string userId)
    {
        var selection = await _selectionRepository.GetSelectionAsync(raceId, userId);
        if (selection is null)
        {
            return [];
        }

        var orderedSelections = selection.OrderedSelections
            .OrderBy(item => item.Position)
            .ToList();

        var drivers = await _driverRepository.GetDriversAsync();
        var driverLookup = drivers
            .Where(driver => !string.IsNullOrWhiteSpace(driver.DriverId))
            .ToDictionary(driver => driver.DriverId!, driver => driver.FullName ?? driver.DriverId!, StringComparer.OrdinalIgnoreCase);

        var rows = new List<CurrentSelectionDto>(orderedSelections.Count);
        foreach (var selectionItem in orderedSelections)
        {
            if (string.IsNullOrWhiteSpace(selectionItem.DriverId))
            {
                continue;
            }

            rows.Add(new CurrentSelectionDto
            {
                Position = selectionItem.Position,
                UserId = selection.UserId,
                UserName = selection.UserId,
                DriverId = selectionItem.DriverId,
                DriverName = driverLookup.GetValueOrDefault(selectionItem.DriverId, selectionItem.DriverId),
                SelectionType = selection.BetType.ToString(),
                Timestamp = selection.SubmittedAtUtc
            });
        }

        return rows;
    }

    public async Task<RaceConfigDto?> GetRaceConfigAsync(string raceId)
    {
        var race = await _raceRepository.GetRaceAsync(raceId);
        if (race is not null)
        {
            var rules = _selectionRuleProvider.GetRules(race, _dateTimeProvider.UtcNow);
            return new RaceConfigDto
            {
                RaceId = race.Id,
                PreQualyDeadlineUtc = race.PreQualyDeadlineUtc,
                FinalDeadlineUtc = race.FinalDeadlineUtc,
                EarlyLockBetType = rules.EarlyLockBetType,
                EarlyLockLabel = rules.EarlyLockLabel,
                FinalSubmissionLabel = rules.FinalSubmissionLabel,
                LockMessage = rules.LockMessage,
                LockedSelectionMessage = rules.LockedSelectionMessage,
                BetOptions = rules.BetOptions
                    .Select(option => new BetOptionDto
                    {
                        BetType = option.BetType,
                        Label = option.Label,
                        IsAvailable = option.IsAvailable
                    })
                    .ToList()
            };
        }

        return null;
    }

    public int CalculateScore(BetType betType, bool isPerfectTopFive, int basePoints, bool submittedBeforePreQualyDeadline)
    {
        if (betType == BetType.AllOrNothing)
        {
            return isPerfectTopFive ? 200 : 0;
        }

        if (betType == BetType.PreQualy && submittedBeforePreQualyDeadline)
        {
            return (int)Math.Round(basePoints * 1.5m, MidpointRounding.AwayFromZero);
        }

        return basePoints;
    }

    private static void ValidateSelections(List<SelectionPosition> selections)
    {
        var validSelections = selections
            .Where(item => !string.IsNullOrWhiteSpace(item.DriverId))
            .ToList();

        var distinctPositions = validSelections
            .Select(item => item.Position)
            .Distinct()
            .Count();

        var distinctCount = selections
            .Where(item => !string.IsNullOrWhiteSpace(item.DriverId))
            .Select(item => item.DriverId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var totalCount = selections.Count;
        if (totalCount != 5 || validSelections.Count != 5 || distinctCount != 5 || distinctPositions != 5)
        {
            throw new SelectionValidationException("Exactly 5 unique drivers must be selected.");
        }

        if (validSelections.Any(item => item.Position < 1 || item.Position > 5))
        {
            throw new SelectionValidationException("Selection positions must be between 1 and 5.");
        }
    }

    private bool IsLocked(Selection selection, Race race, DateTime nowUtc)
    {
        var rules = _selectionRuleProvider.GetRules(race, nowUtc);
        return nowUtc > race.FinalDeadlineUtc
            || (rules.LocksAtEarlyDeadline(selection.BetType) && nowUtc > race.PreQualyDeadlineUtc);
    }
}
