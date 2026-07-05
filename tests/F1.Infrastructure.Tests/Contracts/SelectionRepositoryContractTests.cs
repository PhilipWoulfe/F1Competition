using F1.Core.Interfaces;
using F1.Core.Models;

namespace F1.Infrastructure.Tests.Contracts;

public abstract class SelectionRepositoryContractTests
{
    // Returns a repository where no selections exist (queries return nothing).
    protected abstract ISelectionRepository CreateEmptyRepository();

    // Returns a repository and a pre-existing selection already persisted to it.
    protected abstract Task<(ISelectionRepository Repo, Selection Existing)> ArrangeRepositoryWithSelection(
        string raceId, string userId, Selection selection);

    [Fact]
    public async Task GetSelectionAsync_ReturnsNull_WhenNoSelectionExists()
    {
        var repository = CreateEmptyRepository();

        var result = await repository.GetSelectionAsync("no-such-race", "user@example.com");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetSelectionAsync_ReturnsSelection_WhenExists()
    {
        var raceId = "main-2026-1-test-grand-prix";
        var userId = "user@example.com";
        var selection = new Selection
        {
            RaceId = raceId,
            UserId = userId,
            BetType = BetType.Regular,
            OrderedSelections =
            [
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" }
            ]
        };

        var (repository, _) = await ArrangeRepositoryWithSelection(raceId, userId, selection);

        var result = await repository.GetSelectionAsync(raceId, userId);

        Assert.NotNull(result);
        Assert.Equal(raceId, result!.RaceId);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(BetType.Regular, result.BetType);
    }

    [Fact]
    public async Task UpsertSelectionAsync_CreatesNewSelection()
    {
        var repository = CreateEmptyRepository();
        var selection = new Selection
        {
            RaceId = "main-2026-2-test-grand-prix",
            UserId = "new@example.com",
            BetType = BetType.Regular,
            OrderedSelections = [new SelectionPosition { Position = 1, DriverId = "verstappen" }]
        };

        var result = await repository.UpsertSelectionAsync(selection);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal("main-2026-2-test-grand-prix", result.RaceId);
        Assert.Equal("new@example.com", result.UserId);
    }

    [Fact]
    public async Task UpsertSelectionAsync_UpdatesExistingSelection_PreservesId()
    {
        var raceId = "main-2026-3-test-grand-prix";
        var userId = "user@example.com";
        var initial = new Selection
        {
            RaceId = raceId,
            UserId = userId,
            BetType = BetType.Regular,
            OrderedSelections = [new SelectionPosition { Position = 1, DriverId = "norris" }]
        };

        var (repository, existing) = await ArrangeRepositoryWithSelection(raceId, userId, initial);
        var originalId = existing.Id;

        var updated = new Selection
        {
            Id = originalId,
            RaceId = raceId,
            UserId = userId,
            BetType = BetType.PreQualy,
            OrderedSelections = [new SelectionPosition { Position = 1, DriverId = "leclerc" }]
        };

        var result = await repository.UpsertSelectionAsync(updated);

        Assert.Equal(originalId, result.Id);
        Assert.Equal(BetType.PreQualy, result.BetType);
    }

    [Fact]
    public async Task UpsertSelectionAsync_PreservesOrderedSelectionsWithPositions()
    {
        var raceId = "main-2026-4-test-grand-prix";
        var userId = "user@example.com";
        var positions = new List<SelectionPosition>
        {
            new() { Position = 1, DriverId = "norris" },
            new() { Position = 2, DriverId = "leclerc" },
            new() { Position = 3, DriverId = "hamilton" },
            new() { Position = 4, DriverId = "piastri" },
            new() { Position = 5, DriverId = "verstappen" }
        };

        var (repository, _) = await ArrangeRepositoryWithSelection(raceId, userId, new Selection
        {
            RaceId = raceId,
            UserId = userId,
            BetType = BetType.Regular,
            OrderedSelections = positions
        });

        var result = await repository.GetSelectionAsync(raceId, userId);

        Assert.NotNull(result);
        Assert.Equal(5, result!.OrderedSelections.Count);

        var ordered = result.OrderedSelections.OrderBy(p => p.Position).ToList();
        Assert.Equal("norris", ordered[0].DriverId);
        Assert.Equal("leclerc", ordered[1].DriverId);
        Assert.Equal("hamilton", ordered[2].DriverId);
        Assert.Equal("piastri", ordered[3].DriverId);
        Assert.Equal("verstappen", ordered[4].DriverId);
    }
}
