using System.Net;
using F1.Web.Models;
using F1.Web.Services.Api;

namespace F1.Web.Services;

public interface ISelectionPageService
{
    Task<SelectionPageLoadResult> LoadAsync(string raceId, int selectionSize, DateTime nowUtc, CancellationToken cancellationToken = default);
    Task<SelectionPageSaveResult> SaveAsync(string raceId, SelectionFormModel formModel, CancellationToken cancellationToken = default);
    string GetSaveErrorMessage(ApiServiceException exception);
}

public sealed class SelectionPageService(
    IDriversApiService driversApiService,
    ISelectionApiService selectionApiService,
    IRaceMetadataApiService raceMetadataApiService) : ISelectionPageService
{
    public async Task<SelectionPageLoadResult> LoadAsync(string raceId, int selectionSize, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raceId);
        if (selectionSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(selectionSize), "Selection size must be greater than zero.");
        }

        var drivers = await driversApiService.GetAllAsync(cancellationToken);
        var raceConfig = await selectionApiService.GetConfigAsync(raceId, cancellationToken);
        var raceMetadata = await raceMetadataApiService.GetPublishedAsync(raceId, cancellationToken);

        var state = CreateDefaultState(selectionSize);

        var existing = await selectionApiService.GetMineAsync(raceId, cancellationToken);
        if (existing is not null)
        {
            state = CreateStateFromSelection(existing, selectionSize);
        }

        var snapshot = await selectionApiService.GetCurrentAsync(raceId, cancellationToken);
        ApplySnapshot(snapshot, state);

        if (!state.IsReadOnly && nowUtc >= raceConfig.FinalDeadlineUtc)
        {
            state = state with { IsReadOnly = true };
        }

        return new SelectionPageLoadResult
        {
            Drivers = drivers,
            RaceConfig = raceConfig,
            RaceMetadata = raceMetadata,
            State = state
        };
    }

    public async Task<SelectionPageSaveResult> SaveAsync(string raceId, SelectionFormModel formModel, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raceId);
        ArgumentNullException.ThrowIfNull(formModel);

        var submission = new SelectionSubmission
        {
            BetType = formModel.SelectedBetType,
            OrderedSelections = formModel.SelectedDriverIds
                .Select((driverId, index) => new SelectionPosition { Position = index + 1, DriverId = driverId })
                .ToList(),
            Selections = formModel.SelectedDriverIds.ToList()
        };

        var saved = await selectionApiService.SaveMineAsync(raceId, submission, cancellationToken);

        var state = CreateStateFromSelection(saved, formModel.SelectedDriverIds.Count);
        var snapshot = await selectionApiService.GetCurrentAsync(raceId, cancellationToken);
        ApplySnapshot(snapshot, state);

        return new SelectionPageSaveResult
        {
            State = state
        };
    }

    public string GetSaveErrorMessage(ApiServiceException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.Error.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "You are not authorized to save this selection.",
            _ => exception.Error.Message
        };
    }

    private static SelectionPageState CreateDefaultState(int selectionSize)
    {
        return new SelectionPageState
        {
            SelectedDriverIds = Enumerable.Repeat(string.Empty, selectionSize).ToList(),
            SelectedBetType = BetType.Regular,
            IsReadOnly = false
        };
    }

    private static SelectionPageState CreateStateFromSelection(Selection selection, int selectionSize)
    {
        var ids = Enumerable.Repeat(string.Empty, selectionSize).ToList();
        var selectedIds = (selection.OrderedSelections ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.DriverId))
            .OrderBy(item => item.Position)
            .Select(item => item.DriverId)
            .ToList();

        if (selectedIds.Count == 0)
        {
            selectedIds = selection.Selections ?? [];
        }

        for (var i = 0; i < ids.Count; i++)
        {
            ids[i] = i < selectedIds.Count ? selectedIds[i] : string.Empty;
        }

        return new SelectionPageState
        {
            SelectedDriverIds = ids,
            SelectedBetType = selection.BetType,
            IsReadOnly = selection.IsLocked
        };
    }

    private static void ApplySnapshot(CurrentSelectionItem[] snapshot, SelectionPageState state)
    {
        if (snapshot.Length == 0)
        {
            return;
        }

        var rankedDrivers = snapshot
            .Where(row => !string.IsNullOrWhiteSpace(row.DriverId))
            .OrderBy(row => row.Position <= 0 ? int.MaxValue : row.Position)
            .ThenBy(row => row.Timestamp)
            .Select(row => row.DriverId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(state.SelectedDriverIds.Count)
            .ToList();

        for (var i = 0; i < state.SelectedDriverIds.Count; i++)
        {
            state.SelectedDriverIds[i] = i < rankedDrivers.Count ? rankedDrivers[i] : string.Empty;
        }

        var selectionType = snapshot
            .Select(row => row.SelectionType)
            .FirstOrDefault(type => !string.IsNullOrWhiteSpace(type));

        if (Enum.TryParse<BetType>(selectionType, ignoreCase: true, out var mappedBetType))
        {
            state.SelectedBetType = mappedBetType;
        }
    }
}

public sealed class SelectionPageLoadResult
{
    public required Driver[] Drivers { get; init; }
    public required RaceConfig RaceConfig { get; init; }
    public RaceQuestionMetadata? RaceMetadata { get; init; }
    public required SelectionPageState State { get; init; }
}

public sealed class SelectionPageSaveResult
{
    public required SelectionPageState State { get; init; }
}

public sealed record SelectionPageState
{
    public required List<string> SelectedDriverIds { get; init; }
    public required BetType SelectedBetType { get; set; }
    public required bool IsReadOnly { get; init; }
}