using F1.Web.Configuration;
using F1.Web.Models;
using F1.Web.Pages;
using F1.Web.Services;
using F1.Web.Services.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;

namespace F1.Web.Tests.Pages;

public class RaceSelectionTests : BunitContext
{
    public RaceSelectionTests()
    {
        Services.AddSingleton<ITimeProvider, DefaultTimeProvider>();
        Services.AddSingleton<IMockDateService, TestMockDateService>();
        Services.AddSingleton<ISelectionPageService, SelectionPageService>();
        Services.AddSingleton<ISelectionCountdownFormatter, SelectionCountdownFormatter>();
    }

    private static readonly RaceConfig DefaultRaceConfig = new()
    {
        RaceId = "2025-24-yas_marina",
        PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 4, 30, 0, DateTimeKind.Utc),
        FinalDeadlineUtc = new DateTime(2025, 12, 8, 3, 30, 0, DateTimeKind.Utc)
    };

    private static readonly Driver[] DefaultDrivers =
    [
        new Driver { DriverId = "norris",     FullName = "Lando Norris" },
        new Driver { DriverId = "leclerc",    FullName = "Charles Leclerc" },
        new Driver { DriverId = "hamilton",   FullName = "Lewis Hamilton" },
        new Driver { DriverId = "piastri",    FullName = "Oscar Piastri" },
        new Driver { DriverId = "verstappen", FullName = "Max Verstappen" },
    ];

    private (Mock<IDriversApiService> drivers, Mock<ISelectionApiService> selection, Mock<IRaceMetadataApiService> metadata)
        RegisterDefaultMocks(
            Driver[]? drivers = null,
            RaceConfig? config = null,
            RaceQuestionMetadata? metadata = null,
            Selection? mySelection = null,
            CurrentSelectionItem[]? current = null)
    {
        var driversMock = new Mock<IDriversApiService>();
        driversMock
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(drivers ?? DefaultDrivers);

        var selectionMock = new Mock<ISelectionApiService>();
        selectionMock
            .Setup(s => s.GetConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(config ?? DefaultRaceConfig);
        selectionMock
            .Setup(s => s.GetMineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mySelection);
        selectionMock
            .Setup(s => s.GetCurrentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(current ?? []);

        var metadataMock = new Mock<IRaceMetadataApiService>();
        metadataMock
            .Setup(s => s.GetPublishedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadata);

        Services.AddSingleton<IDriversApiService>(driversMock.Object);
        Services.AddSingleton<ISelectionApiService>(selectionMock.Object);
        Services.AddSingleton<IRaceMetadataApiService>(metadataMock.Object);

        return (driversMock, selectionMock, metadataMock);
    }

    [Fact]
    public void RaceSelection_ShouldRenderWarningAndCountdown_WhenLoadedWithNoExistingSelection()
    {
        RegisterDefaultMocks();

        var cut = RenderForRace("2026-01-australia");

        cut.WaitForAssertion(() => Assert.Contains("Locking for Pre-Qualy gives +50% points", cut.Markup));
        Assert.Contains("Countdown:", cut.Markup);
        Assert.Equal(string.Empty, cut.FindAll("select")[0].GetAttribute("value"));
    }

    [Fact]
    public void RaceSelection_ShouldRenderPublishedRaceQuestions_WhenMetadataExists()
    {
        RegisterDefaultMocks(metadata: new RaceQuestionMetadata
        {
            RaceId = "2025-24-yas_marina",
            H2HQuestion = "Who finishes higher: Leclerc or Norris?",
            BonusQuestion = "How many safety-car laps?",
            IsPublished = true,
            UpdatedAtUtc = DateTime.UtcNow
        });

        var cut = RenderForRace(DefaultRaceConfig.RaceId);

        cut.WaitForAssertion(() => Assert.Contains("Race Questions", cut.Markup));
        Assert.Contains("Who finishes higher: Leclerc or Norris?", cut.Markup);
        Assert.Contains("How many safety-car laps?", cut.Markup);
    }

    [Fact]
    public void RaceSelection_ShouldRenderLockedState_WhenExistingSelectionIsLocked()
    {
        RegisterDefaultMocks(
            mySelection: new Selection
            {
                Id = Guid.NewGuid(),
                RaceId = "2025-24-yas_marina",
                UserId = "user@example.com",
                BetType = BetType.Regular,
                IsLocked = true,
                OrderedSelections =
                [
                    new SelectionPosition { Position = 1, DriverId = "leclerc" },
                    new SelectionPosition { Position = 2, DriverId = "norris" },
                    new SelectionPosition { Position = 3, DriverId = "hamilton" },
                    new SelectionPosition { Position = 4, DriverId = "piastri" },
                    new SelectionPosition { Position = 5, DriverId = "verstappen" },
                ]
            },
            current:
            [
                new CurrentSelectionItem
                {
                    Position = 1, UserId = "user@example.com", UserName = "user@example.com",
                    DriverId = "norris", DriverName = "Lando Norris",
                    SelectionType = "PreQualy", Timestamp = new DateTime(2025, 12, 6, 10, 0, 0, DateTimeKind.Utc)
                },
                new CurrentSelectionItem
                {
                    Position = 2, UserId = "user@example.com", UserName = "user@example.com",
                    DriverId = "leclerc", DriverName = "Charles Leclerc",
                    SelectionType = "PreQualy", Timestamp = new DateTime(2025, 12, 6, 10, 1, 0, DateTimeKind.Utc)
                },
            ]);

        var cut = RenderForRace(DefaultRaceConfig.RaceId);

        cut.WaitForAssertion(() => Assert.Contains("This pre-qualy selection is locked.", cut.Markup));
        Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
        // Snapshot overrides drivers: norris P1, leclerc P2
        Assert.Equal("norris", cut.FindAll("select")[0].GetAttribute("value"));
        Assert.Equal("leclerc", cut.FindAll("select")[1].GetAttribute("value"));
        Assert.True(cut.Find("#strategy-prequaly").HasAttribute("checked"));
    }

    [Fact]
    public void RaceSelection_ShouldSaveSelection_WhenSubmitSucceeds()
    {
        const string raceId = "2026-01-australia";
        var (_, selectionMock, _) = RegisterDefaultMocks(config: new RaceConfig
        {
            RaceId = raceId,
            PreQualyDeadlineUtc = new DateTime(2026, 3, 13, 4, 30, 0, DateTimeKind.Utc),
            FinalDeadlineUtc = new DateTime(2026, 3, 14, 3, 30, 0, DateTimeKind.Utc)
        });
        var savedSelection = new Selection
        {
            RaceId = raceId,
            UserId = "user@example.com",
            BetType = BetType.Regular,
            IsLocked = false,
            OrderedSelections =
            [
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" },
            ],
            Selections = ["norris", "leclerc", "hamilton", "piastri", "verstappen"]
        };

        selectionMock
            .Setup(s => s.SaveMineAsync(It.IsAny<string>(), It.IsAny<SelectionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedSelection);

        var cut = RenderForRace(raceId);
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("select").Count));

        ChangeSelect(cut, 0, "norris");
        ChangeSelect(cut, 1, "leclerc");
        ChangeSelect(cut, 2, "hamilton");
        ChangeSelect(cut, 3, "piastri");
        ChangeSelect(cut, 4, "verstappen");

        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Selection saved successfully.", cut.Markup));
        Assert.Equal("norris", cut.FindAll("select")[0].GetAttribute("value"));
        Assert.Equal("leclerc", cut.FindAll("select")[1].GetAttribute("value"));
        Assert.Equal("hamilton", cut.FindAll("select")[2].GetAttribute("value"));
        Assert.Equal("piastri", cut.FindAll("select")[3].GetAttribute("value"));
        Assert.Equal("verstappen", cut.FindAll("select")[4].GetAttribute("value"));
        selectionMock.Verify(
            s => s.SaveMineAsync(raceId, It.IsAny<SelectionSubmission>(), It.IsAny<CancellationToken>()),
            Times.Once);
        selectionMock.Verify(
            s => s.GetCurrentAsync(raceId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void RaceSelection_ShouldShowApiErrorMessage_WhenSaveFails()
    {
        var (_, selectionMock, _) = RegisterDefaultMocks();

        selectionMock
            .Setup(s => s.SaveMineAsync(It.IsAny<string>(), It.IsAny<SelectionSubmission>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiServiceException(new ApiError(HttpStatusCode.BadRequest, "Exactly 5 unique drivers must be selected.")));

        var cut = RenderForRace(DefaultRaceConfig.RaceId);
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("select").Count));

        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Exactly 5 unique drivers must be selected.", cut.Markup));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void RaceSelection_ShouldShowFriendlyAuthorizationMessage_WhenSaveIsUnauthorized(HttpStatusCode statusCode)
    {
        var (_, selectionMock, _) = RegisterDefaultMocks();

        selectionMock
            .Setup(s => s.SaveMineAsync(It.IsAny<string>(), It.IsAny<SelectionSubmission>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiServiceException(new ApiError(statusCode, $"Saving race selection failed with status code {(int)statusCode}.")));

        var cut = RenderForRace(DefaultRaceConfig.RaceId);
        cut.WaitForAssertion(() => Assert.Equal(5, cut.FindAll("select").Count));

        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => Assert.Contains("You are not authorized to save this selection.", cut.Markup));
    }

    [Fact]
    public async Task RaceSelection_DisposeAsync_ShouldBeIdempotent()
    {
        RegisterDefaultMocks();

        var cut = RenderForRace(DefaultRaceConfig.RaceId);
        cut.WaitForAssertion(() => Assert.Contains("Countdown:", cut.Markup));

        var component = (IAsyncDisposable)cut.Instance;
        await component.DisposeAsync();
        // Second call must not throw
        await component.DisposeAsync();
    }

    [Fact]
    public void RaceSelection_ShouldPopulateControls_FromCurrentSelectionsSnapshot()
    {
        RegisterDefaultMocks(
            drivers:
            [
                new Driver { DriverId = "norris",  FullName = "Lando Norris" },
                new Driver { DriverId = "leclerc", FullName = "Charles Leclerc" },
            ],
            current:
            [
                new CurrentSelectionItem
                {
                    Position = 1, UserId = "user@example.com", UserName = "user@example.com",
                    DriverId = "norris", DriverName = "Lando Norris",
                    SelectionType = "PreQualy", Timestamp = new DateTime(2025, 12, 6, 10, 0, 0, DateTimeKind.Utc)
                },
                new CurrentSelectionItem
                {
                    Position = 2, UserId = "user@example.com", UserName = "user@example.com",
                    DriverId = "leclerc", DriverName = "Charles Leclerc",
                    SelectionType = "PreQualy", Timestamp = new DateTime(2025, 12, 6, 10, 1, 0, DateTimeKind.Utc)
                },
            ]);

        var cut = RenderForRace(DefaultRaceConfig.RaceId);

        cut.WaitForAssertion(() => Assert.Equal("norris", cut.FindAll("select")[0].GetAttribute("value")));
        Assert.Equal("leclerc", cut.FindAll("select")[1].GetAttribute("value"));
        Assert.True(cut.Find("#strategy-prequaly").HasAttribute("checked"));
    }

    [Fact]
    public void RaceSelection_ShouldRenderLockedState_WhenPastFinalDeadline()
    {
        var pastDeadlineConfig = new RaceConfig
        {
            RaceId = "2025-24-yas_marina",
            PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 13, 0, 0, DateTimeKind.Utc),
            FinalDeadlineUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc)
        };

        Services.AddSingleton<ITimeProvider>(new FrozenTimeProvider(new DateTime(2025, 12, 8, 12, 1, 0, DateTimeKind.Utc)));
        RegisterDefaultMocks(config: pastDeadlineConfig);

        var cut = RenderForRace(DefaultRaceConfig.RaceId);

        cut.WaitForAssertion(() => Assert.Contains("All deadlines have passed.", cut.Markup));
        Assert.True(cut.Find("button[type='submit']").HasAttribute("disabled"));
        Assert.True(cut.FindAll("select").All(s => s.HasAttribute("disabled")));
    }

    [Fact]
    public void RaceSelection_ShouldUseCompatibilityRoute_WhenYasMarinaPathIsUsed()
    {
        var (_, selectionMock, _) = RegisterDefaultMocks();
        NavigateTo(SelectionDefaults.CompatibilityRoutePath);

        var cut = Render<RaceSelection>();

        cut.WaitForAssertion(() => Assert.Contains("Countdown:", cut.Markup));
        selectionMock.Verify(
            s => s.GetConfigAsync(SelectionDefaults.CompatibilityRaceId, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void RaceSelection_ShouldShowControlledError_WhenRaceContextMissing()
    {
        RegisterDefaultMocks();

        var cut = Render<RaceSelection>();

        cut.WaitForAssertion(() => Assert.Contains("Race context is missing.", cut.Markup));
    }

    [Fact]
    public void RaceSelection_ShouldShowControlledError_WhenRaceContextInvalid()
    {
        RegisterDefaultMocks();

        var cut = Render<RaceSelection>(parameters => parameters.Add(p => p.RaceId, "bad race id"));

        cut.WaitForAssertion(() => Assert.Contains("Race context is invalid.", cut.Markup));
    }

    private static void ChangeSelect(IRenderedComponent<RaceSelection> cut, int index, string value)
    {
        cut.FindAll("select")[index].Change(value);
    }

    private IRenderedComponent<RaceSelection> RenderForRace(string raceId)
    {
        NavigateTo($"selection/{raceId}");
        return Render<RaceSelection>(parameters => parameters.Add(p => p.RaceId, raceId));
    }

    private void NavigateTo(string relativePath)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(relativePath);
    }

    private sealed class FrozenTimeProvider(DateTime frozenUtc) : ITimeProvider
    {
        public DateTime UtcNow => frozenUtc;
    }

    private sealed class TestMockDateService : IMockDateService
    {
        public DateTime? GetMockDate() => null;
        public Task RefreshAsync() => Task.CompletedTask;
        public Task SetMockDateAsync(DateTime? dateUtc) => Task.CompletedTask;
    }
}


