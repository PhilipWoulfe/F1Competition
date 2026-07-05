using F1.Core.Dtos;
using F1.Core.Interfaces;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using System.Net;
using System.Net.Http.Json;

namespace F1.Api.Tests.Integration;

public class CurrentSelectionsEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestConnectionString = "Host=localhost;Port=5432;Database=f1_tests;Username=f1;Password=f1";
    private const string RaceId = "2026-01-albert_park";

    private readonly WebApplicationFactory<Program> _factory;

    public CurrentSelectionsEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", TestConnectionString);
        _factory = factory;
    }

    [Fact]
    public async Task GetCurrentSelections_ShouldReturnOk_WithJsonArray()
    {
        var serviceMock = new Mock<ISelectionService>();
        serviceMock
            .Setup(service => service.GetCurrentSelectionsAsync(RaceId, "user@example.com"))
            .ReturnsAsync([
                new CurrentSelectionDto
                {
                    Position = 1,
                    UserId = "user@example.com",
                    UserName = "user@example.com",
                    DriverId = "norris",
                    DriverName = "Lando Norris",
                    SelectionType = "Regular",
                    Timestamp = new DateTime(2025, 12, 6, 9, 0, 0, DateTimeKind.Utc)
                }
            ]);

        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    { "ConnectionStrings:Postgres", TestConnectionString },
                    { "DevSettings:SimulateCloudflare", "true" },
                    { "DevSettings:MockEmail", "user@example.com" },
                    { "DevSettings:MockCurrentSelections", "false" }
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISelectionService>();
                services.AddScoped(_ => serviceMock.Object);
            });
        }).CreateClient();

        var response = await client.GetAsync($"/selections/{RaceId}/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<List<CurrentSelectionDto>>();
        Assert.NotNull(payload);
        Assert.Single(payload!);
        Assert.Equal(1, payload[0].Position);
        Assert.Equal("norris", payload[0].DriverId);
    }
}
