using F1.Api.Dtos;
using F1.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using System.Text.Encodings.Web;

namespace F1.Api.Tests.Integration;

public sealed class CompetitionLeaderboardRouteAccessIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestConnectionString = "Host=localhost;Port=5432;Database=f1_tests;Username=f1;Password=f1";

    private readonly WebApplicationFactory<Program> factory;

    public CompetitionLeaderboardRouteAccessIntegrationTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", TestConnectionString);
        this.factory = factory;
    }

    [Fact]
    public async Task LeaderboardRoute_WhenAnonymous_ShouldReturnUnauthorized()
    {
        var client = CreateClient(null, null);

        var response = await client.GetAsync("/races/results?competition=philip&season=2025&view=active");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LeaderboardRoute_WhenAuthenticatedNonAdminRequestsComparison_ShouldReturnForbidden()
    {
        var client = CreateClient("user@example.com", ["F1 Users"]);

        var response = await client.GetAsync("/races/results?competition=philip&season=2025&view=recalculated");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task LeaderboardRoute_WhenAuthenticatedAdminRequestsComparison_ShouldReturnOk()
    {
        var client = CreateClient("admin@example.com", ["F1 Admins"]);

        var response = await client.GetAsync("/races/results?competition=philip&season=2025&view=recalculated");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateClient(string? mockEmail, string[]? mockGroups)
    {
        var service = new Mock<ICompetitionLeaderboardService>();
        service
            .Setup(x => x.GetLeaderboardAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CompetitionLeaderboardResponseDto(
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
                Items: [new CompetitionLeaderboardEntryDto(1, "Alice", 25, 25, 20)]));

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = TestConnectionString,
                    ["Database:AutoMigrate"] = "false",
                    ["DevSettings:SimulateCloudflare"] = "true",
                    ["DevSettings:MockEmail"] = string.Empty,
                    ["CloudflareAccess:AdminGroups:0"] = "F1 Admins"
                };

                if (!string.IsNullOrWhiteSpace(mockEmail))
                {
                    values["DevSettings:MockEmail"] = mockEmail;
                }

                if (mockGroups is not null)
                {
                    for (var i = 0; i < mockGroups.Length; i++)
                    {
                        values[$"DevSettings:MockGroups:{i}"] = mockGroups[i];
                    }
                }

                config.AddInMemoryCollection(values);
            });

            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("IntegrationTest")
                    .AddScheme<AuthenticationSchemeOptions, IntegrationTestAuthHandler>("IntegrationTest", _ => { });

                services.RemoveAll<ICompetitionLeaderboardService>();
                services.AddScoped(_ => service.Object);
            });
        }).CreateClient();
    }

    private sealed class IntegrationTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public IntegrationTestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }
    }
}