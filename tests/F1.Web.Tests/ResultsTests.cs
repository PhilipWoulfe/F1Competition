using Bunit.TestDoubles;
using F1.Web.Configuration;
using F1.Web.Models;
using F1.Web.Pages;
using F1.Web.Services;
using F1.Web.Services.Api;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;

namespace F1.Web.Tests.Pages;

public class ResultsTests : BunitContext
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly InMemorySelectionContextStore _selectionContextStore = new();
    private readonly Mock<IMigrationRunsApiService> _migrationRunsApiMock = new();

    public ResultsTests()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("user@example.com");

        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost")
        };
        Services.AddSingleton(_httpClient);
        _migrationRunsApiMock
            .Setup(api => api.GetRunsAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminMigrationRunListResponse(1, 3, 1,
            [
                new AdminMigrationRunListItem(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "Completed",
                    true,
                    "/tmp/import.csv",
                    "abc123",
                    new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 7, 7, 10, 5, 0, DateTimeKind.Utc),
                    10,
                    0,
                    0,
                    0,
                    0,
                    0,
                    null)
            ]));
        Services.AddSingleton(_migrationRunsApiMock.Object);
        Services.AddSingleton<ISelectionContextStore>(_selectionContextStore);
        Services.AddSingleton<ISelectionContextService, SelectionContextService>();
        Services.Configure<SelectionContextOptions>(options =>
        {
            options.Options =
            [
                new SelectionContextOption { CompetitionSlug = "main", CompetitionLabel = "Main", Season = 2026, DefaultRound = 1 },
                new SelectionContextOption { CompetitionSlug = "philip", CompetitionLabel = "Philip", Season = 2025, DefaultRound = 1 }
            ];
        });
        _selectionContextStore.StoredContext = new StoredSelectionContext("philip", 2025);
    }

    [Fact]
    public void Results_ShouldRenderLoading_WhenDataIsBeingFetched()
    {
        // Arrange
        var tcs = new TaskCompletionSource<HttpResponseMessage>();
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(tcs.Task);

        // Act
        var cut = Render<Results>();

        // Assert
        Assert.Contains("Loading leaderboard...", cut.Markup);

        // Cleanup
        tcs.SetResult(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("[]")
        });
    }

    [Fact]
    public void Results_ShouldRenderTable_WhenApiReturnsData()
    {
        // Arrange
        var mockResults = new CompetitionLeaderboardResponse(
            CompetitionSlug: "philip",
            Season: 2025,
            DisplayName: "Philip 2025",
            ActiveScoreSource: "ImportedLegacy",
            ScoreView: "active",
            ScoreSourceLabel: "Official Source: Imported legacy scores",
            ScoreSourceHelperText: "Official standings use imported legacy totals.",
            IsComparisonAvailable: false,
            IsDataAvailable: true,
            EmptyStateMessage: null,
            SourceRunId: Guid.NewGuid(),
            Items:
            [
                new CompetitionLeaderboardEntry(1, "Alice", 25, 25, 20),
                new CompetitionLeaderboardEntry(2, "Bob", 18, 18, 24)
            ]);

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(mockResults))
        };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var cut = Render<Results>();

        // Assert
        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);
        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Alice", rows[0].InnerHtml);
        Assert.Contains("Bob", rows[1].InnerHtml);
        Assert.Contains("Official Source: Imported legacy scores", cut.Markup);
    }

    [Fact]
    public void Results_ShouldNotBeEmpty_WhenApiReturnsData()
    {
        // Arrange
        var mockResults = new CompetitionLeaderboardResponse(
            CompetitionSlug: "philip",
            Season: 2025,
            DisplayName: "Philip 2025",
            ActiveScoreSource: "ImportedLegacy",
            ScoreView: "active",
            ScoreSourceLabel: "Official Source: Imported legacy scores",
            ScoreSourceHelperText: "Official standings use imported legacy totals.",
            IsComparisonAvailable: false,
            IsDataAvailable: true,
            EmptyStateMessage: null,
            SourceRunId: Guid.NewGuid(),
            Items:
            [
                new CompetitionLeaderboardEntry(1, "Alice", 25, 25, 20),
                new CompetitionLeaderboardEntry(2, "Bob", 18, 18, 24)
            ]);

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(mockResults))
        };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var cut = Render<Results>();

        // Assert
        cut.WaitForState(() => cut.FindAll("tbody tr").Count > 0);
        var rows = cut.FindAll("tbody tr");
        Assert.NotEmpty(rows);
    }

    [Fact]
    public void Results_ShouldShowError_WhenApiCallFails()
    {
        // Arrange
        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("API is down"));

        // Act
        var cut = Render<Results>();

        // Assert
        cut.WaitForState(() => cut.FindAll("p").Count > 0);
        Assert.Contains("API is down", cut.Markup);
    }

    [Fact]
    public void Results_ShouldShowEmptyState_WhenLeaderboardIsUnavailable()
    {
        var mockResults = new CompetitionLeaderboardResponse(
            CompetitionSlug: "main",
            Season: 2026,
            DisplayName: "Main 2026",
            ActiveScoreSource: "ImportedLegacy",
            ScoreView: "active",
            ScoreSourceLabel: "Official Source: Imported legacy scores",
            ScoreSourceHelperText: "Official standings use imported legacy totals.",
            IsComparisonAvailable: false,
            IsDataAvailable: false,
            EmptyStateMessage: "Leaderboard data is not available for this competition yet.",
            SourceRunId: null,
            Items: []);

        var response = new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(mockResults))
        };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        // Act
        var cut = Render<Results>();

        cut.WaitForAssertion(() => Assert.Contains("Leaderboard data is not available for this competition yet.", cut.Markup));
    }

    [Fact]
    public void Results_ShouldShowAdminCompareToggle_WhenAdmin()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetRoles("Admin");

        var mockResults = new CompetitionLeaderboardResponse(
            CompetitionSlug: "philip",
            Season: 2025,
            DisplayName: "Philip 2025",
            ActiveScoreSource: "ImportedLegacy",
            ScoreView: "active",
            ScoreSourceLabel: "Official Source: Imported legacy scores",
            ScoreSourceHelperText: "Official standings use imported legacy totals.",
            IsComparisonAvailable: true,
            IsDataAvailable: true,
            EmptyStateMessage: null,
            SourceRunId: Guid.NewGuid(),
            Items: [new CompetitionLeaderboardEntry(1, "Alice", 25, 25, 20)]);

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(mockResults))
            });

        var cut = Render<Results>();

        cut.WaitForAssertion(() => Assert.Contains("Official", cut.Markup));
        Assert.Contains("Recalculated", cut.Markup);
        Assert.Contains("Recent Migration Runs", cut.Markup);
        Assert.Contains("Completed", cut.Markup);
    }

    [Fact]
    public void Results_ShouldRenderParticipantDrilldown_WhenParticipantIsSelected()
    {
        var leaderboardResponse = new CompetitionLeaderboardResponse(
            CompetitionSlug: "philip",
            Season: 2025,
            DisplayName: "Philip 2025",
            ActiveScoreSource: "ImportedLegacy",
            ScoreView: "active",
            ScoreSourceLabel: "Official Source: Imported legacy scores",
            ScoreSourceHelperText: "Official standings use imported legacy totals.",
            IsComparisonAvailable: false,
            IsDataAvailable: true,
            EmptyStateMessage: null,
            SourceRunId: Guid.NewGuid(),
            Items: [new CompetitionLeaderboardEntry(1, "Alice", 25, 25, 20)]);

        var detailResponse = new CompetitionParticipantDetailResponse(
            CompetitionSlug: "philip",
            Season: 2025,
            DisplayName: "Philip 2025",
            ParticipantName: "Alice",
            RacePicks: new CompetitionParticipantSectionSummary(
                "Race Picks",
                3,
                5,
                [new CompetitionParticipantDetailItem("AUS", "1", 3, 5, 2, "RACE_CORRECT", "Exact pick")]),
            Preseason: new CompetitionParticipantSectionSummary(
                "Preseason Questions",
                4,
                6,
                [new CompetitionParticipantDetailItem("WDC", "Who wins the championship?", 4, 6, 2, "PRESEASON_CORRECT", "Matched answer")]),
            H2h: new CompetitionParticipantSectionSummary(
                "H2H Questions",
                0,
                0,
                []));

        _handlerMock
            .Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(leaderboardResponse))
            })
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(detailResponse))
            });

        var cut = Render<Results>();

        cut.WaitForAssertion(() => Assert.Contains("Alice", cut.Markup));
        cut.Find("button.participant-drilldown").Click();

        cut.WaitForAssertion(() => Assert.Contains("Race Picks", cut.Markup));
        Assert.Contains("Preseason Questions", cut.Markup);
        Assert.Contains("No H2H question data is available for this participant.", cut.Markup);
    }

    private sealed class InMemorySelectionContextStore : ISelectionContextStore
    {
        public StoredSelectionContext? StoredContext { get; set; }

        public Task<StoredSelectionContext?> GetAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(StoredContext);
        }

        public Task SaveAsync(StoredSelectionContext context, CancellationToken cancellationToken = default)
        {
            StoredContext = context;
            return Task.CompletedTask;
        }
    }
}
