using F1.Web.Models;
using F1.Web.Pages;
using F1.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Net;

namespace F1.Web.Tests.Pages;

public sealed class AdminMigrationRunsTests : BunitContext
{
    [Fact]
    public void AdminMigrationRuns_ShouldExposeTablistAndSelectedTabAria_WhenRunIsSelected()
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
                    UnexpectedTotalDeltaPoints: 3,
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
            UnexpectedTotalDeltaPoints: 3,
            UnresolvedTokenSummary:
            [
                new AdminMigrationUnresolvedTokenSummary("MAXX", 2, 12, new DateTime(2026, 7, 6, 11, 0, 1, DateTimeKind.Utc))
            ],
            ParticipantDeltas: [],
            PreseasonSummary: new AdminMigrationPreseasonSummary(0, 0, 0, 0),
            PreseasonParticipantDeltas: [],
            PreseasonQuestionDiffs: [],
            PreseasonReasonCategorySummaries: [],
            RaceDiffs: [],
            PickDiffs: []);

        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);
        apiMock
            .Setup(x => x.GetRunDetailAsync(runId, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(detailResponse);

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();
        cut.WaitForAssertion(() => Assert.Contains("Completed", cut.Markup));

        cut.Find("button.btn.btn-sm.btn-outline-primary").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("ul[role='tablist']"));
            Assert.True(cut.FindAll("button[role='tab'][aria-selected='true']").Count >= 1);
        });
    }

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
                    UnexpectedTotalDeltaPoints: 3,
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
            UnexpectedTotalDeltaPoints: 3,
            UnresolvedTokenSummary:
            [
                new AdminMigrationUnresolvedTokenSummary("MAXX", 2, 12, new DateTime(2026, 7, 6, 11, 0, 1, DateTimeKind.Utc))
            ],
            ParticipantDeltas:
            [
                new AdminMigrationParticipantDelta("Philip", 500, 496, -4, "PODIUM_RULE_VARIANCE", 2),
                new AdminMigrationParticipantDelta("Alex", 500, 500, 0, "EXACT_MATCH", 5)
            ],
            PreseasonSummary: new AdminMigrationPreseasonSummary(2, 2, 1, -20),
            PreseasonParticipantDeltas:
            [
                new AdminMigrationPreseasonParticipantDelta("Morgan", 40, 20, -20, "PRESEASON_RULE_VARIANCE", 1),
                new AdminMigrationPreseasonParticipantDelta("Taylor", 20, 20, 0, "PRESEASON_POINTS_MATCH", 1)
            ],
            PreseasonQuestionDiffs:
            [
                new AdminMigrationPreseasonQuestionDiff(22, "PRE-022", "Who wins the constructors title?", "Morgan", 20, 0, -20, "PRESEASON_RULE_VARIANCE", "Mismatch"),
                new AdminMigrationPreseasonQuestionDiff(23, "PRE-023", "Who wins Bahrain?", "Taylor", 20, 20, 0, "PRESEASON_POINTS_MATCH", "Match")
            ],
            PreseasonReasonCategorySummaries:
            [
                new AdminMigrationPreseasonReasonCategorySummary("PRESEASON_RULE_VARIANCE", 1, -20)
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

        var unexpectedDetailResponse = new AdminMigrationRunDetailResponse(
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
            PickDiffCount: 1,
            RaceDiffCount: 1,
            TotalDeltaPoints: -4,
            UnexpectedTotalDeltaPoints: -4,
            UnresolvedTokenSummary:
            [
                new AdminMigrationUnresolvedTokenSummary("MAXX", 2, 12, new DateTime(2026, 7, 6, 11, 0, 1, DateTimeKind.Utc))
            ],
            ParticipantDeltas:
            [
                new AdminMigrationParticipantDelta("Philip", 500, 496, -4, "PODIUM_RULE_VARIANCE", 2)
            ],
            PreseasonSummary: new AdminMigrationPreseasonSummary(1, 1, 1, -20),
            PreseasonParticipantDeltas:
            [
                new AdminMigrationPreseasonParticipantDelta("Morgan", 40, 20, -20, "PRESEASON_RULE_VARIANCE", 1)
            ],
            PreseasonQuestionDiffs:
            [
                new AdminMigrationPreseasonQuestionDiff(22, "PRE-022", "Who wins the constructors title?", "Morgan", 20, 0, -20, "PRESEASON_RULE_VARIANCE", "Mismatch")
            ],
            PreseasonReasonCategorySummaries:
            [
                new AdminMigrationPreseasonReasonCategorySummary("PRESEASON_RULE_VARIANCE", 1, -20)
            ],
            RaceDiffs:
            [
                new AdminMigrationRaceDiff("albert_park", "Philip", 25, 20, -5, "PODIUM_RULE_VARIANCE", "Podium mismatch")
            ],
            PickDiffs:
            [
                new AdminMigrationPickDiff("monza", "DNF", "Alex", 5, 5, 0, "EXACT_MATCH", "No variance")
            ]);

        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);
        apiMock
            .Setup(x => x.GetRunDetailAsync(runId, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync(detailResponse);
        apiMock
            .Setup(x => x.GetRunDetailAsync(runId, It.IsAny<CancellationToken>(), "unexpected"))
            .ReturnsAsync(unexpectedDetailResponse);
        apiMock
            .Setup(x => x.GetRunDiffExportUrl(runId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((Guid id, string exportType, string format, string? expectedStatus) =>
                $"admin/migration-runs/{id}/exports/{exportType}?format={format}");

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();

        cut.WaitForAssertion(() => Assert.Contains("Completed", cut.Markup));
        Assert.Contains("Migration Runs", cut.Markup);
        Assert.Contains(runId.ToString(), cut.Markup);
        Assert.Contains(cut.FindAll("span.badge.bg-success"), element => element.TextContent.Contains("Completed", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll("span.badge.bg-secondary"), element => element.TextContent.Contains("Dry-run", StringComparison.Ordinal));
        Assert.True(cut.FindAll("span.badge.bg-warning.text-dark").Count >= 2);

        cut.Find("button.btn.btn-sm.btn-outline-primary").Click();

        cut.WaitForAssertion(() => Assert.Contains("Run Detail", cut.Markup));
        Assert.Contains("Hide kickoff", cut.Markup);
        Assert.Contains("Participant Comparisons", cut.Markup);
        Assert.Contains("Expected vs Actual Review", cut.Markup);
        Assert.Contains("Race Comparisons", cut.Markup);
        Assert.Contains("Pick Comparisons", cut.Markup);
        Assert.Contains("Unexpected: 3", cut.Markup);
        Assert.Contains("Question Diffs", cut.Markup);
        Assert.Contains(cut.FindAll("button.nav-link"), element => element.TextContent.Contains("Overview", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll("button.nav-link"), element => element.TextContent.Contains("Preseason", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll("button.nav-link"), element => element.TextContent.Contains("Race Participants", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll("button.nav-link"), element => element.TextContent.Contains("Race Diffs", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll("button.nav-link"), element => element.TextContent.Contains("Pick Diffs", StringComparison.Ordinal));
        Assert.Contains(cut.FindAll("button.nav-link"), element => element.TextContent.Contains("Exports", StringComparison.Ordinal));

        var signOffCard = cut.Find("[data-testid='exports-signoff-card']");
        var preseasonCard = cut.Find("[data-testid='exports-preseason-card']");
        var raceCard = cut.Find("[data-testid='exports-race-card']");

        Assert.Contains("Sign-off Package", signOffCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Preseason Reconciliation", preseasonCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Participant Diffs", raceCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Participant diffs CSV", signOffCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Pick diffs CSV", signOffCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Preseason question diffs CSV", preseasonCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Preseason participant diffs CSV", preseasonCard.TextContent, StringComparison.Ordinal);
        Assert.Contains("Participant diffs CSV", raceCard.TextContent, StringComparison.Ordinal);

        Assert.Contains("Podium mismatch", cut.Markup);
        Assert.Contains("Wrong slot", cut.Markup);
        Assert.Contains("Who wins the constructors title?", cut.Markup);
        Assert.DoesNotContain("PRESEASON_POINTS_MATCH", cut.Markup);

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

        cut.Find("#detail-non-zero-only").Change(false);
        cut.Find("#detail-expected-status").Change("unexpected");
        cut.WaitForAssertion(() => Assert.DoesNotContain("Wrong slot", cut.Markup));

        cut.Find("#preseason-participant-filter").Change("Morgan");
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Morgan", cut.Markup);
            Assert.DoesNotContain("Taylor", cut.Markup);
        });

        cut.Find("#preseason-participant-filter").Change(string.Empty);
        cut.Find("#preseason-non-zero-only").Change(true);
        cut.WaitForAssertion(() => Assert.DoesNotContain("PRESEASON_POINTS_MATCH", cut.Markup));

        cut.Find("button.btn.btn-outline-primary").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("New run", cut.Markup);
            Assert.DoesNotContain("Start Migration Run", cut.Markup);
        });

        cut.Find("button.btn.btn-outline-primary").Click();
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Hide kickoff", cut.Markup);
            Assert.Contains("Start Migration Run", cut.Markup);
        });
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
                    UnexpectedTotalDeltaPoints: -4,
                    ErrorMessage: null)
            ]);

        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse);
        apiMock
            .Setup(x => x.GetRunDetailAsync(runId, It.IsAny<CancellationToken>(), null))
            .ReturnsAsync((AdminMigrationRunDetailResponse?)null);

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();

        cut.WaitForAssertion(() => Assert.Contains(runId.ToString(), cut.Markup));

        cut.Find("button.btn.btn-sm.btn-outline-primary").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains($"Migration run detail was not found for run {runId}.", cut.Markup));
        Assert.DoesNotContain("Run Detail", cut.Markup);
    }

    [Fact]
    public void AdminMigrationRuns_ShouldRequireConfirmation_AndShowSuccess_WhenKickoffStarts()
    {
        var runId = Guid.NewGuid();
        var listResponse = new AdminMigrationRunListResponse(1, 25, 0, []);

        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .SetupSequence(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listResponse)
            .ReturnsAsync(listResponse);
        apiMock
            .Setup(x => x.StartRunAsync(
                It.IsAny<AdminMigrationRunKickoffRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminMigrationRunKickoffResponse(
                RunId: runId,
                Status: "Started",
                IsDryRun: true,
                RequestedMode: "dry-run",
                SourceFilePath: "/tmp/import.csv",
                SourceFileChecksum: "abc123",
                TriggeredAtUtc: new DateTime(2026, 7, 6, 14, 0, 0, DateTimeKind.Utc),
                RequestedBy: "admin@example.com"));

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();
        cut.WaitForAssertion(() => Assert.Contains("Start Migration Run", cut.Markup));

        cut.Find("#kickoff-source-path").Change("/tmp/import.csv");
        cut.Find("#kickoff-mode").Change("dry-run");
        cut.Find("button.btn.btn-success").Click();

        cut.WaitForAssertion(() => Assert.Contains("Confirm Migration Kickoff", cut.Markup));
        cut.Find("button.btn.btn-danger").Click();

        cut.WaitForAssertion(() => Assert.Contains($"Migration run {runId} started in dry-run mode", cut.Markup));
        apiMock.Verify(x => x.StartRunAsync(
            It.Is<AdminMigrationRunKickoffRequest>(request =>
                request.SourceFilePath == "/tmp/import.csv" &&
                request.Mode == "dry-run"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AdminMigrationRuns_ShouldShowConflictError_WhenKickoffConflicts()
    {
        var apiMock = new Mock<IMigrationRunsApiService>();
        apiMock
            .Setup(x => x.GetRunsAsync(1, 25, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminMigrationRunListResponse(1, 25, 0, []));
        apiMock
            .Setup(x => x.StartRunAsync(
                It.IsAny<AdminMigrationRunKickoffRequest>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ApiServiceException(new ApiError(
                HttpStatusCode.Conflict,
                "An active migration run already exists for this source/checksum.",
                "active_run_conflict")));

        Services.AddSingleton(apiMock.Object);

        var cut = Render<AdminMigrationRuns>();
        cut.WaitForAssertion(() => Assert.Contains("Start Migration Run", cut.Markup));

        cut.Find("button.btn.btn-success").Click();
        cut.WaitForAssertion(() => Assert.Contains("Confirm Migration Kickoff", cut.Markup));
        cut.Find("button.btn.btn-danger").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("Failed to start migration run: An active migration run already exists for this source/checksum.", cut.Markup));
    }
}
