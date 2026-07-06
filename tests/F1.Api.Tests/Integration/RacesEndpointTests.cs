using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using System.Net;

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
            }).CreateClient();

            // Act
            var response = await client.GetAsync("/races/results");

            // Assert
            response.EnsureSuccessStatusCode();
        }
    }
}
