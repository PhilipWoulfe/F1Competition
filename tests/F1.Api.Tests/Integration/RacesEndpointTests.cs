using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using F1.Api.Dtos;
using F1.Api.Services;
using Moq;

namespace F1.Api.Tests.Integration
{
    public class RacesEndpointTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestConnectionString = "Host=localhost;Port=5432;Database=f1_tests;Username=f1;Password=f1";

        private readonly WebApplicationFactory<Program> _factory;

        public RacesEndpointTests(WebApplicationFactory<Program> factory)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", TestConnectionString);
            _factory = factory;
        }

        [Fact]
        public async Task GetRacesResults_ShouldReturnUnauthorized_WhenSimulateCloudflareIsFalse()
        {
            // Arrange
            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "ConnectionStrings:Postgres", TestConnectionString },
                        { "Database:AutoMigrate", "false" },
                        { "DevSettings:SimulateCloudflare", "false" }
                    });
                });
            }).CreateClient();

            // Act
            var response = await client.GetAsync("/races/results");

            // Assert
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task GetRacesResults_ShouldReturnOk_WhenSimulateCloudflareIsTrue()
        {
            // Arrange
            var leaderboardService = new Mock<ICompetitionLeaderboardService>();
            leaderboardService
                .Setup(service => service.GetLeaderboardAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CompetitionLeaderboardResponseDto(
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
                    Items: [new CompetitionLeaderboardEntryDto(1, "Alice", 25, 25, 20)]));

            var client = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "ConnectionStrings:Postgres", TestConnectionString },
                        { "Database:AutoMigrate", "false" },
                        { "DevSettings:SimulateCloudflare", "true" }
                    });
                });

                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ICompetitionLeaderboardService>();
                    services.AddScoped(_ => leaderboardService.Object);
                });
            }).CreateClient();

            // Act
            var response = await client.GetAsync("/races/results?competition=philip&season=2025&view=active");

            // Assert
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<CompetitionLeaderboardResponseDto>();
            Assert.NotNull(payload);
            Assert.Equal("philip", payload!.CompetitionSlug);
            Assert.Equal(2025, payload.Season);
        }
    }
}
