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

    public SelectionService(
        ISelectionRepository selectionRepository,
        IDriverRepository driverRepository,
        IRaceRepository raceRepository,
        IDateTimeProvider dateTimeProvider)
    {
        _selectionRepository = selectionRepository;
        _driverRepository = driverRepository;
        _raceRepository = raceRepository;
        _dateTimeProvider = dateTimeProvider;
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
        selection.IsLocked = race is not null && (IsPreQualyLocked(selection, race, nowUtc) || nowUtc > race.FinalDeadlineUtc);
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
        var existingSelection = await _selectionRepository.GetSelectionAsync(raceId, userId);

        if (existingSelection is not null && IsPreQualyLocked(existingSelection, race, nowUtc))
        {
            throw new SelectionForbiddenException("Pre-Qualy locked selections cannot be edited after the race-specific pre-qualy deadline.");
        }

        if (submission.BetType == BetType.PreQualy && nowUtc > race.PreQualyDeadlineUtc)
        {
            throw new SelectionValidationException("Pre-Qualy strategy is no longer available after the race-specific pre-qualy deadline.");
        }

        if (nowUtc > race.FinalDeadlineUtc)
        {
            throw new SelectionForbiddenException("Selections are locked after the race-specific final deadline.");
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

    public RaceConfigDto? GetRaceConfig(string raceId)
    {
        var race = _raceRepository.GetRaceAsync(raceId).GetAwaiter().GetResult();
        if (race is not null)
        {
            return new RaceConfigDto
            {
                RaceId = race.Id,
                PreQualyDeadlineUtc = race.PreQualyDeadlineUtc,
                FinalDeadlineUtc = race.FinalDeadlineUtc
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

    private static bool IsPreQualyLocked(Selection selection, Race race, DateTime nowUtc)
    {
        return selection.BetType == BetType.PreQualy && nowUtc > race.PreQualyDeadlineUtc;
    }
}
