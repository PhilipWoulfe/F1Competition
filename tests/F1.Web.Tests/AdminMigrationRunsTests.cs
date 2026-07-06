using F1.Web.Models;
using F1.Web.Pages;
using F1.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace F1.Web.Tests.Pages;

public sealed class AdminMigrationRunsTests : BunitContext
{
    [Fact]
    public void AdminMigrationRuns_ShouldRenderRunsTable_WhenApiReturnsItems()
    {
        var runId = Guid.Parse("79b8a33f-68f8-4d42-9d30-73ebcbcf61d7");
        var listResponse = new AdminMigrationRunListResponse(
            Page: 1,
            PageSize: 25,
            TotalCount: 1,
            Items:
            [
                new AdminMigrationRunListItem(
                    RunId: runId,
                    Status: "Completed",
                    IsDryRun: true,
                    SourceFilePath: "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
                    SourceFileChecksum: "abc123",
                    StartedAtUtc: new DateTime(2026, 7, 6, 11, 0, 0, DateTimeKind.Utc),
                    FinishedAtUtc: new DateTime(2026, 7, 6, 11, 2, 0, DateTimeKind.Utc),
                    RawRowCount: 200,
                    UnresolvedTokenCount: 3,
                    PickDiffCount: 100,
                    RaceDiffCount: 20,
                    TotalDeltaPoints: -4,
                    ErrorMessage: null)
            ]);

        var detailResponse = new AdminMigrationRunDetailResponse(
            RunId: runId,
            Status: "Completed",
            IsDryRun: true,
            SourceFilePath: "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
            SourceFileChecksum: "abc123",
            StartedAtUtc: new DateTime(2026, 7, 6, 11, 0, 0, DateTimeKind.Utc),
            FinishedAtUtc: new DateTime(2026, 7, 6, 11, 2, 0, DateTimeKind.Utc),
            RawRowCount: 200,
            ErrorMessage: null,
            UnresolvedTokenCount: 3,
            PickDiffCount: 100,
            RaceDiffCount: 20,
            TotalDeltaPoints: -4,
            UnresolvedTokenSummary:
            [
                new AdminMigrationUnresolvedTokenSummary("MAXX", 2, 12, new DateTime(2026, 7, 6, 11, 0, 1, DateTimeKind.Utc))
            ],
            ParticipantDeltas:
            [
                new AdminMigrationParticipantDelta("Philip", 500, 496, -4, "PODIUM_RULE_VARIANCE", 2),
                new AdminMigrationParticipantDelta("Alex", 500, 500, 0, "EXACT_MATCH", 5)
            ],
            RaceDiffs:
            [
                new AdminMigrationRaceDiff("albert_park", "Philip", 25, 20, -5, "PODIUM_RULE_VARIANCE", "Podium mismatch"),
                new AdminMigrationRaceDiff("monza", "Alex", 20, 20, 0, "EXACT_MATCH", "No variance")
            ],
            PickDiffs:
            [
                new AdminMigrationPickDiff("albert_park", "1", "Philip", 10, 5, -5, "PODIUM_RULE_VARIANCE", "Wrong slot"),
                new AdminMigrationPickDiff("monza", "DNF", "Alex", 5, 5, 0, "EXACT_MATCH", "No variance")
            ]);

        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);
        apiMock
            .Setup(x => x.GetRunDetailAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detailResponse);

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();

        cut.WaitForAssertion(() => Assert.Contains("Completed", cut.Markup));
        Assert.Contains("Migration Runs", cut.Markup);
        Assert.Contains(runId.ToString(), cut.Markup);

        cut.Find("button.btn.btn-sm.btn-outline-primary").Click();

        cut.WaitForAssertion(() => Assert.Contains("Run Detail", cut.Markup));
        Assert.Contains("Participant Comparisons", cut.Markup);
        Assert.Contains("Race Comparisons", cut.Markup);
        Assert.Contains("Pick Comparisons", cut.Markup);
        Assert.Contains("Podium mismatch", cut.Markup);
        Assert.Contains("Wrong slot", cut.Markup);

        cut.Find("#detail-participant-filter").Change("Philip");
        cut.WaitForAssertion(() => Assert.DoesNotContain("No variance", cut.Markup));

        cut.Find("#detail-participant-filter").Change(string.Empty);
        cut.Find("#detail-race-filter").Change("albert");
        cut.Find("#detail-reason-filter").Change("podium");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Podium mismatch", cut.Markup);
            Assert.DoesNotContain("No variance", cut.Markup);
        });

        cut.Find("#detail-race-filter").Change(string.Empty);
        cut.Find("#detail-reason-filter").Change(string.Empty);
        cut.Find("#detail-non-zero-only").Change(true);
        cut.WaitForAssertion(() => Assert.DoesNotContain("Alex", cut.Markup));
    }

    [Fact]
    public void AdminMigrationRuns_ShouldRenderEmptyState_WhenNoItemsReturned()
    {
        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminMigrationRunListResponse(1, 25, 0, []));

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();

        cut.WaitForAssertion(() =>
            Assert.Contains("No migration runs found for the selected filters.", cut.Markup));
    }

    [Fact]
    public void AdminMigrationRuns_ShouldRenderError_WhenApiFails()
    {
        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();

        cut.WaitForAssertion(() =>
            Assert.Contains("Failed to load migration runs: boom", cut.Markup));
    }

    [Fact]
    public void AdminMigrationRuns_ShouldRenderNotFoundMessage_WhenRunDetailIsMissing()
    {
        var runId = Guid.Parse("79b8a33f-68f8-4d42-9d30-73ebcbcf61d7");
        var listResponse = new AdminMigrationRunListResponse(
            Page: 1,
            PageSize: 25,
            TotalCount: 1,
            Items:
            [
                new AdminMigrationRunListItem(
                    RunId: runId,
                    Status: "Completed",
                    IsDryRun: true,
                    SourceFilePath: "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
                    SourceFileChecksum: "abc123",
                    StartedAtUtc: new DateTime(2026, 7, 6, 11, 0, 0, DateTimeKind.Utc),
                    FinishedAtUtc: new DateTime(2026, 7, 6, 11, 2, 0, DateTimeKind.Utc),
                    RawRowCount: 200,
                    UnresolvedTokenCount: 3,
                    PickDiffCount: 100,
                    RaceDiffCount: 20,
                    TotalDeltaPoints: -4,
                    ErrorMessage: null)
            ]);

        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);
        apiMock
            .Setup(x => x.GetRunDetailAsync(runId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminMigrationRunDetailResponse?)null);

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();

        cut.WaitForAssertion(() => Assert.Contains(runId.ToString(), cut.Markup));

        cut.Find("button.btn.btn-sm.btn-outline-primary").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains($"Migration run detail was not found for run {runId}.", cut.Markup));
        Assert.DoesNotContain("Run Detail", cut.Markup);
    }
}
