using F1.Core.Dtos;
using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Services;
using Moq;

namespace F1.Api.Tests;

public class SelectionServiceTests
{
    private const string YasMarinaRaceId = "2025-24-yas_marina";
    private const string PhilipYasMarinaRaceId = "philip-2025-2025-24-yas-marina";
    private const string AlbertParkRaceId = "2026-01-albert_park";

    private readonly Mock<ISelectionRepository> _selectionRepositoryMock = new();
    private readonly Mock<IDriverRepository> _driverRepositoryMock = new();
    private readonly Mock<IRaceRepository> _raceRepositoryMock = new();
    private readonly Mock<IDateTimeProvider> _dateTimeProviderMock = new();
    private readonly ISelectionRuleProvider _selectionRuleProvider = new SelectionRuleProvider();

    [Fact]
    public async Task UpsertSelectionAsync_ShouldReject_WhenMoreThanFiveSelectionsSubmitted()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 7, 0, 0, 0, DateTimeKind.Utc));

        var submission = new SelectionSubmissionDto
        {
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" },
                new SelectionPosition { Position = 0, DriverId = "" }
            }
        };

        await Assert.ThrowsAsync<SelectionValidationException>(() =>
            service.UpsertSelectionAsync(YasMarinaRaceId, "user@example.com", submission));
    }

    [Fact]
    public async Task UpsertSelectionAsync_ShouldThrowSelectionRaceNotFoundException_WhenRaceDoesNotExist()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 7, 0, 0, 0, DateTimeKind.Utc));
        _raceRepositoryMock
            .Setup(repo => repo.GetRaceAsync("no-such-race"))
            .ReturnsAsync((Race?)null);

        var submission = new SelectionSubmissionDto
        {
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        await Assert.ThrowsAsync<SelectionRaceNotFoundException>(() =>
            service.UpsertSelectionAsync("no-such-race", "user@example.com", submission));
    }

    [Fact]
    public async Task UpsertSelectionAsync_ShouldRejectPreQualyBet_AfterRaceDeadline()
    {
        var service = CreateServiceAt(new DateTime(2026, 3, 15, 4, 1, 0, DateTimeKind.Utc));
        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(AlbertParkRaceId, "user@example.com"))
            .ReturnsAsync((Selection?)null);

        var submission = new SelectionSubmissionDto
        {
            BetType = BetType.PreQualy,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        await Assert.ThrowsAsync<SelectionValidationException>(() =>
            service.UpsertSelectionAsync(AlbertParkRaceId, "user@example.com", submission));
    }

    [Fact]
    public async Task UpsertSelectionAsync_ShouldAllowRegularUpdate_AfterPreQualyDeadlineBeforeFinal()
    {
        var nowUtc = new DateTime(2026, 3, 15, 4, 30, 0, DateTimeKind.Utc);
        var service = CreateServiceAt(nowUtc);

        var existing = new Selection
        {
            Id = Guid.NewGuid(),
            RaceId = AlbertParkRaceId,
            UserId = "user@example.com",
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(AlbertParkRaceId, "user@example.com"))
            .ReturnsAsync(existing);

        _selectionRepositoryMock
            .Setup(repo => repo.UpsertSelectionAsync(It.IsAny<Selection>()))
            .ReturnsAsync((Selection selection) => selection);

        var submission = new SelectionSubmissionDto
        {
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        var updated = await service.UpsertSelectionAsync(AlbertParkRaceId, "user@example.com", submission);

        Assert.Equal(BetType.Regular, updated.BetType);
        Assert.Equal(nowUtc, updated.SubmittedAtUtc);
        Assert.Equal("norris", updated.OrderedSelections[0].DriverId);
    }

    [Fact]
    public async Task UpsertSelectionAsync_ShouldReportFinalDeadline_WhenExistingSelectionIsEditedAfterFinalDeadline()
    {
        var service = CreateServiceAt(new DateTime(2026, 3, 15, 6, 1, 0, DateTimeKind.Utc));

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(AlbertParkRaceId, "user@example.com"))
            .ReturnsAsync(new Selection
            {
                Id = Guid.NewGuid(),
                RaceId = AlbertParkRaceId,
                UserId = "user@example.com",
                BetType = BetType.Regular,
                OrderedSelections = new List<SelectionPosition>
                {
                    new SelectionPosition { Position = 1, DriverId = "norris" },
                    new SelectionPosition { Position = 2, DriverId = "leclerc" },
                    new SelectionPosition { Position = 3, DriverId = "hamilton" },
                    new SelectionPosition { Position = 4, DriverId = "piastri" },
                    new SelectionPosition { Position = 5, DriverId = "verstappen" }
                }
            });

        var submission = new SelectionSubmissionDto
        {
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        var ex = await Assert.ThrowsAsync<SelectionForbiddenException>(() =>
            service.UpsertSelectionAsync(AlbertParkRaceId, "user@example.com", submission));

        Assert.Equal("Selections are locked after 2026-03-15 06:00:00Z.", ex.Message);
    }

    [Fact]
    public async Task GetRaceConfigAsync_ShouldReturnConfig_ForAnyKnownRaceId()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc));

        var config = await service.GetRaceConfigAsync(AlbertParkRaceId);

        Assert.NotNull(config);
        Assert.Equal(AlbertParkRaceId, config.RaceId);
        Assert.Equal(new DateTime(2026, 3, 15, 4, 0, 0, DateTimeKind.Utc), config.PreQualyDeadlineUtc);
        Assert.Equal(new DateTime(2026, 3, 15, 6, 0, 0, DateTimeKind.Utc), config.FinalDeadlineUtc);
        Assert.Equal(BetType.PreQualy, config.EarlyLockBetType);
        Assert.Equal("Pre-Qualy lock", config.EarlyLockLabel);
        Assert.Contains(config.BetOptions, option => option.BetType == BetType.AllOrNothing && option.IsAvailable);
    }

    [Fact]
    public async Task GetRaceConfigAsync_ShouldReturnTopThreeConfig_ForPhilip2025Competition()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc));

        var config = await service.GetRaceConfigAsync(PhilipYasMarinaRaceId);

        Assert.NotNull(config);
        Assert.Equal(3, config.SelectionCount);
    }

    [Fact]
    public async Task GetRaceConfigAsync_ShouldReturnNull_ForUnknownRace()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc));

        var config = await service.GetRaceConfigAsync("unknown-race");

        Assert.Null(config);
    }

    [Fact]
    public async Task GetSelectionAsync_ShouldReturnIsLocked_AfterFinalSubmissionDeadline()
    {
        var service = CreateServiceAt(new DateTime(2026, 3, 15, 6, 1, 0, DateTimeKind.Utc));

        var existing = new Selection
        {
            Id = Guid.NewGuid(),
            RaceId = AlbertParkRaceId,
            UserId = "user@example.com",
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(AlbertParkRaceId, "user@example.com"))
            .ReturnsAsync(existing);

        var result = await service.GetSelectionAsync(AlbertParkRaceId, "user@example.com");

        Assert.NotNull(result);
        Assert.True(result.IsLocked);
    }

    [Fact]
    public async Task GetSelectionAsync_ShouldStayUnlocked_BeforeRaceFinalDeadline()
    {
        var service = CreateServiceAt(new DateTime(2026, 3, 15, 5, 30, 0, DateTimeKind.Utc));

        var existing = new Selection
        {
            Id = Guid.NewGuid(),
            RaceId = AlbertParkRaceId,
            UserId = "user@example.com",
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(AlbertParkRaceId, "user@example.com"))
            .ReturnsAsync(existing);

        var result = await service.GetSelectionAsync(AlbertParkRaceId, "user@example.com");

        Assert.NotNull(result);
        Assert.False(result.IsLocked);
    }

    [Fact]
    public async Task GetSelectionAsync_ShouldDefaultToLocked_WhenRaceCannotBeLoaded()
    {
        var service = CreateServiceAt(new DateTime(2026, 3, 15, 5, 30, 0, DateTimeKind.Utc));

        var existing = new Selection
        {
            Id = Guid.NewGuid(),
            RaceId = "unknown-race",
            UserId = "user@example.com",
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync("unknown-race", "user@example.com"))
            .ReturnsAsync(existing);

        var result = await service.GetSelectionAsync("unknown-race", "user@example.com");

        Assert.NotNull(result);
        Assert.True(result.IsLocked);
    }

    [Fact]
    public void CalculateScore_ShouldNotApplyPreQualyMultiplier_ForAllOrNothing()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc));

        var score = service.CalculateScore(
            BetType.AllOrNothing,
            isPerfectTopFive: true,
            basePoints: 100,
            submittedBeforePreQualyDeadline: true);

        Assert.Equal(200, score);
    }

    [Fact]
    public async Task GetCurrentSelectionsAsync_ShouldReturnMappedRows_WhenSelectionExists()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc));

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(YasMarinaRaceId, "user@example.com"))
            .ReturnsAsync(new Selection
            {
                Id = Guid.NewGuid(),
                RaceId = YasMarinaRaceId,
                UserId = "user@example.com",
                BetType = BetType.PreQualy,
                SubmittedAtUtc = new DateTime(2025, 12, 6, 10, 0, 0, DateTimeKind.Utc),
                OrderedSelections = new List<SelectionPosition>
                {
                    new SelectionPosition { Position = 1, DriverId = "norris" },
                    new SelectionPosition { Position = 2, DriverId = "leclerc" }
                }
            });

        _driverRepositoryMock
            .Setup(repo => repo.GetDriversAsync())
            .ReturnsAsync([
                new Driver { DriverId = "norris", FullName = "Lando Norris" },
                new Driver { DriverId = "leclerc", FullName = "Charles Leclerc" }
            ]);

        var rows = await service.GetCurrentSelectionsAsync(YasMarinaRaceId, "user@example.com");

        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].Position);
        Assert.Equal("user@example.com", rows[0].UserId);
        Assert.Equal("Lando Norris", rows[0].DriverName);
        Assert.Equal("PreQualy", rows[0].SelectionType);
        Assert.Equal(2, rows[1].Position);
    }

    [Fact]
    public async Task GetCurrentSelectionsAsync_ShouldReturnRowsSortedByPosition_WhenSelectionsAreOutOfOrder()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc));

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(YasMarinaRaceId, "user@example.com"))
            .ReturnsAsync(new Selection
            {
                Id = Guid.NewGuid(),
                RaceId = YasMarinaRaceId,
                UserId = "user@example.com",
                BetType = BetType.Regular,
                SubmittedAtUtc = new DateTime(2025, 12, 6, 10, 0, 0, DateTimeKind.Utc),
                OrderedSelections = new List<SelectionPosition>
                {
                    new SelectionPosition { Position = 3, DriverId = "hamilton" },
                    new SelectionPosition { Position = 1, DriverId = "norris" },
                    new SelectionPosition { Position = 5, DriverId = "verstappen" },
                    new SelectionPosition { Position = 2, DriverId = "leclerc" },
                    new SelectionPosition { Position = 4, DriverId = "piastri" }
                }
            });

        _driverRepositoryMock
            .Setup(repo => repo.GetDriversAsync())
            .ReturnsAsync([
                new Driver { DriverId = "norris", FullName = "Lando Norris" },
                new Driver { DriverId = "leclerc", FullName = "Charles Leclerc" },
                new Driver { DriverId = "hamilton", FullName = "Lewis Hamilton" },
                new Driver { DriverId = "piastri", FullName = "Oscar Piastri" },
                new Driver { DriverId = "verstappen", FullName = "Max Verstappen" }
            ]);

        var rows = await service.GetCurrentSelectionsAsync(YasMarinaRaceId, "user@example.com");

        Assert.Equal(5, rows.Count);
        Assert.Equal(1, rows[0].Position);
        Assert.Equal("norris", rows[0].DriverId);
        Assert.Equal(2, rows[1].Position);
        Assert.Equal("leclerc", rows[1].DriverId);
        Assert.Equal(3, rows[2].Position);
        Assert.Equal("hamilton", rows[2].DriverId);
        Assert.Equal(4, rows[3].Position);
        Assert.Equal("piastri", rows[3].DriverId);
        Assert.Equal(5, rows[4].Position);
        Assert.Equal("verstappen", rows[4].DriverId);
    }

    [Fact]
    public async Task GetCurrentSelectionsAsync_ShouldReturnEmpty_WhenNoSelectionExists()
    {
        var service = CreateServiceAt(new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc));

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(YasMarinaRaceId, "user@example.com"))
            .ReturnsAsync((Selection?)null);

        var rows = await service.GetCurrentSelectionsAsync(YasMarinaRaceId, "user@example.com");

        Assert.Empty(rows);
        _driverRepositoryMock.Verify(repo => repo.GetDriversAsync(), Times.Never);
    }

    [Fact]
    public async Task UpsertSelectionAsync_ShouldAllowPreQualyBet_BeforeDeadline()
    {
        var beforeDeadline = new DateTime(2026, 3, 15, 3, 59, 0, DateTimeKind.Utc);
        var service = CreateServiceAt(beforeDeadline);

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(AlbertParkRaceId, "user@example.com"))
            .ReturnsAsync((Selection?)null);

        _selectionRepositoryMock
            .Setup(repo => repo.UpsertSelectionAsync(It.IsAny<Selection>()))
            .ReturnsAsync((Selection selection) => selection);

        var submission = new SelectionSubmissionDto
        {
            BetType = BetType.PreQualy,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        };

        var result = await service.UpsertSelectionAsync(AlbertParkRaceId, "user@example.com", submission);

        Assert.NotNull(result);
        Assert.Equal(BetType.PreQualy, result.BetType);
        Assert.Equal(beforeDeadline, result.SubmittedAtUtc);
        Assert.Equal("norris", result.OrderedSelections[0].DriverId);
    }

    [Fact]
    public async Task UpsertSelectionAsync_ShouldAllowThreeSelections_ForPhilip2025Competition()
    {
        var nowUtc = new DateTime(2025, 12, 6, 12, 0, 0, DateTimeKind.Utc);
        var service = CreateServiceAt(nowUtc);

        _selectionRepositoryMock
            .Setup(repo => repo.GetSelectionAsync(PhilipYasMarinaRaceId, "user@example.com"))
            .ReturnsAsync((Selection?)null);

        _selectionRepositoryMock
            .Setup(repo => repo.UpsertSelectionAsync(It.IsAny<Selection>()))
            .ReturnsAsync((Selection selection) => selection);

        var submission = new SelectionSubmissionDto
        {
            BetType = BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" }
            }
        };

        var result = await service.UpsertSelectionAsync(PhilipYasMarinaRaceId, "user@example.com", submission);

        Assert.NotNull(result);
        Assert.Equal(3, result.OrderedSelections.Count);
        Assert.Equal(nowUtc, result.SubmittedAtUtc);
    }

    [Fact]
    public async Task GetRaceConfigAsync_ShouldMarkEarlyLockBetUnavailable_AfterDeadline_ForNonYasRace()
    {
        var service = CreateServiceAt(new DateTime(2026, 3, 15, 4, 1, 0, DateTimeKind.Utc));

        var config = await service.GetRaceConfigAsync(AlbertParkRaceId);

        Assert.NotNull(config);
        var preQualyOption = Assert.Single(config.BetOptions, option => option.BetType == BetType.PreQualy);
        Assert.False(preQualyOption.IsAvailable);
        Assert.Contains("15 Mar 2026 04:00 UTC", config.LockMessage, StringComparison.Ordinal);
    }

    private SelectionService CreateServiceAt(DateTime utcNow)
    {
        _dateTimeProviderMock.Setup(clock => clock.UtcNow).Returns(utcNow);
        _raceRepositoryMock
            .Setup(repo => repo.GetRaceAsync(It.IsAny<string>()))
            .ReturnsAsync((string raceId) => CreateRace(raceId));

        return new SelectionService(
            _selectionRepositoryMock.Object,
            _driverRepositoryMock.Object,
            _raceRepositoryMock.Object,
            _dateTimeProviderMock.Object,
            _selectionRuleProvider);
    }

    private static Race? CreateRace(string raceId)
    {
        return raceId switch
        {
            YasMarinaRaceId => new Race
            {
                Id = YasMarinaRaceId,
                PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 13, 0, 0, DateTimeKind.Utc),
                FinalDeadlineUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc)
            },
            PhilipYasMarinaRaceId => new Race
            {
                Id = PhilipYasMarinaRaceId,
                Season = 2025,
                PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 13, 0, 0, DateTimeKind.Utc),
                FinalDeadlineUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc)
            },
            AlbertParkRaceId => new Race
            {
                Id = AlbertParkRaceId,
                Season = 2026,
                PreQualyDeadlineUtc = new DateTime(2026, 3, 15, 4, 0, 0, DateTimeKind.Utc),
                FinalDeadlineUtc = new DateTime(2026, 3, 15, 6, 0, 0, DateTimeKind.Utc)
            },
            _ => null
        };
    }
}
