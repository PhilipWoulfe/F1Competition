using System.Net;
using F1.Web.Models;
using F1.Web.Services;
using F1.Web.Services.Api;
using Moq;

namespace F1.Web.Tests.Services;

public class SelectionPageServiceTests
{
    [Fact]
    public async Task LoadAsync_WhenExistingAndSnapshotExists_AppliesSnapshotOrderAndSelectionType()
    {
        var driversApi = new Mock<IDriversApiService>();
        var selectionApi = new Mock<ISelectionApiService>();
        var metadataApi = new Mock<IRaceMetadataApiService>();

        driversApi
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new Driver { DriverId = "norris", FullName = "Lando Norris" },
                new Driver { DriverId = "leclerc", FullName = "Charles Leclerc" }
            ]);
        selectionApi
            .Setup(s => s.GetConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRaceConfig());
        selectionApi
            .Setup(s => s.GetMineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Selection
            {
                BetType = BetType.Regular,
                IsLocked = false,
                OrderedSelections =
                [
                    new SelectionPosition { Position = 1, DriverId = "leclerc" },
                    new SelectionPosition { Position = 2, DriverId = "norris" }
                ]
            });
        selectionApi
            .Setup(s => s.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CurrentSelectionItem
                {
                    Position = 1,
                    DriverId = "norris",
                    SelectionType = "PreQualy",
                    Timestamp = new DateTime(2025, 12, 6, 10, 0, 0, DateTimeKind.Utc)
                },
                new CurrentSelectionItem
                {
                    Position = 2,
                    DriverId = "leclerc",
                    SelectionType = "PreQualy",
                    Timestamp = new DateTime(2025, 12, 6, 10, 1, 0, DateTimeKind.Utc)
                }
            ]);
        metadataApi
            .Setup(s => s.GetPublishedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaceQuestionMetadata?)null);

        var sut = new SelectionPageService(driversApi.Object, selectionApi.Object, metadataApi.Object);

        var result = await sut.LoadAsync("2025-24-yas_marina", 5, new DateTime(2025, 12, 6, 9, 0, 0, DateTimeKind.Utc));

        Assert.Equal("norris", result.State.SelectedDriverIds[0]);
        Assert.Equal("leclerc", result.State.SelectedDriverIds[1]);
        Assert.Equal(BetType.PreQualy, result.State.SelectedBetType);
        Assert.False(result.State.IsReadOnly);
    }

    [Fact]
    public async Task LoadAsync_WhenPastFinalDeadline_ForceLocksSelection()
    {
        var driversApi = new Mock<IDriversApiService>();
        var selectionApi = new Mock<ISelectionApiService>();
        var metadataApi = new Mock<IRaceMetadataApiService>();

        driversApi
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Driver>());
        selectionApi
            .Setup(s => s.GetConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRaceConfig());
        selectionApi
            .Setup(s => s.GetMineAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Selection?)null);
        selectionApi
            .Setup(s => s.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CurrentSelectionItem>());
        metadataApi
            .Setup(s => s.GetPublishedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaceQuestionMetadata?)null);

        var sut = new SelectionPageService(driversApi.Object, selectionApi.Object, metadataApi.Object);

        var result = await sut.LoadAsync("2025-24-yas_marina", 5, new DateTime(2025, 12, 8, 12, 0, 1, DateTimeKind.Utc));

        Assert.True(result.State.IsReadOnly);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void GetSaveErrorMessage_WhenAuthFailure_ReturnsFriendlyMessage(HttpStatusCode statusCode)
    {
        var sut = new SelectionPageService(
            Mock.Of<IDriversApiService>(),
            Mock.Of<ISelectionApiService>(),
            Mock.Of<IRaceMetadataApiService>());

        var error = new ApiServiceException(new ApiError(statusCode, "Backend auth error"));

        var result = sut.GetSaveErrorMessage(error);

        Assert.Equal("You are not authorized to save this selection.", result);
    }

    [Fact]
    public async Task SaveAsync_WhenSuccessful_UsesSavedAndSnapshotState()
    {
        var driversApi = new Mock<IDriversApiService>();
        var selectionApi = new Mock<ISelectionApiService>();
        var metadataApi = new Mock<IRaceMetadataApiService>();

        selectionApi
            .Setup(s => s.SaveMineAsync(It.IsAny<string>(), It.IsAny<SelectionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Selection
            {
                BetType = BetType.Regular,
                IsLocked = false,
                OrderedSelections =
                [
                    new SelectionPosition { Position = 1, DriverId = "leclerc" },
                    new SelectionPosition { Position = 2, DriverId = "norris" }
                ]
            });
        selectionApi
            .Setup(s => s.GetCurrentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new CurrentSelectionItem
                {
                    Position = 1,
                    DriverId = "norris",
                    SelectionType = "PreQualy",
                    Timestamp = new DateTime(2025, 12, 6, 10, 0, 0, DateTimeKind.Utc)
                }
            ]);

        var sut = new SelectionPageService(driversApi.Object, selectionApi.Object, metadataApi.Object);
        var form = new SelectionFormModel();
        form.SelectedDriverIds[0] = "leclerc";
        form.SelectedDriverIds[1] = "norris";
        form.SelectedBetType = BetType.Regular;

        var result = await sut.SaveAsync("2025-24-yas_marina", form);

        Assert.Equal("norris", result.State.SelectedDriverIds[0]);
        Assert.Equal(BetType.PreQualy, result.State.SelectedBetType);
        selectionApi.Verify(s => s.SaveMineAsync("2025-24-yas_marina", It.IsAny<SelectionSubmission>(), It.IsAny<CancellationToken>()), Times.Once);
        selectionApi.Verify(s => s.GetCurrentAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static RaceConfig CreateRaceConfig()
    {
        return new RaceConfig
        {
            RaceId = "2025-24-yas_marina",
            PreQualyDeadlineUtc = new DateTime(2025, 12, 7, 13, 0, 0, DateTimeKind.Utc),
            FinalDeadlineUtc = new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc)
        };
    }
}