using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using F1.Api;
using F1.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace F1.Api.Tests.Integration
{
    public class MockDateControllerTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private const string TestConnectionString = "Host=localhost;Port=5432;Database=f1_tests;Username=f1;Password=f1";

        private readonly WebApplicationFactory<Program> _factory;

        public MockDateControllerTests(WebApplicationFactory<Program> factory)
        {
            Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", TestConnectionString);
            _factory = factory;
        }

        [Fact]
        public async Task Can_Set_And_Get_MockDate()
        {
            var client = CreateClient();
            var setDate = new DateTime(2025, 12, 21, 15, 0, 0, DateTimeKind.Utc);
            var setResponse = await client.PostAsJsonAsync("/admin/mock-date", new { mockDateUtc = setDate });
            Assert.Equal(HttpStatusCode.NoContent, setResponse.StatusCode);

            var getResponse = await client.GetAsync("/admin/mock-date");
            getResponse.EnsureSuccessStatusCode();
            var result = await getResponse.Content.ReadFromJsonAsync<GetMockDateResponse>();
            Assert.NotNull(result);
            Assert.Equal(setDate, result.mockDate);
        }

        [Fact]
        public async Task Can_Clear_MockDate()
        {
            var client = CreateClient();
            var setResponse = await client.PostAsJsonAsync("/admin/mock-date", new { mockDateUtc = (DateTime?)null });
            Assert.Equal(HttpStatusCode.NoContent, setResponse.StatusCode);

            var getResponse = await client.GetAsync("/admin/mock-date");
            getResponse.EnsureSuccessStatusCode();
            var result = await getResponse.Content.ReadFromJsonAsync<GetMockDateResponse>();
            Assert.NotNull(result);
            Assert.Null(result.mockDate);
        }

        [Fact]
        public async Task Get_MockDate_ShouldReturnOk_ForNonAdminUser()
        {
            var client = CreateNonAdminClient();

            var response = await client.GetAsync("/admin/mock-date");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        [Fact]
        public async Task Set_MockDate_ShouldReturnForbidden_ForNonAdminUser()
        {
            var client = CreateNonAdminClient();

            var response = await client.PostAsJsonAsync("/admin/mock-date", new { mockDateUtc = DateTime.UtcNow });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        private HttpClient CreateNonAdminClient()
        {
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "ConnectionStrings:Postgres", TestConnectionString },
                        { "Database:AutoMigrate", "false" },
                        { "DevSettings:SimulateCloudflare", "true" },
                        { "DevSettings:MockEmail", "user@example.com" },
                        { "DevSettings:MockGroups:0", "F1 Users" },
                        { "CloudflareAccess:AdminGroups:0", "F1 Admins" }
                    });
                });

                builder.ConfigureServices(services =>
                {
                    services.AddAuthentication("IntegrationTest")
                        .AddScheme<AuthenticationSchemeOptions, IntegrationTestAuthHandler>("IntegrationTest", _ => { });
                });
            }).CreateClient();
        }

        private HttpClient CreateClient()
        {
            return _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "ConnectionStrings:Postgres", TestConnectionString },
                        { "Database:AutoMigrate", "false" }
                    });
                });
            }).CreateClient();
        }

        private class GetMockDateResponse
        {
            public DateTime? mockDate { get; set; }
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
                // Keep CloudflareAccessMiddleware as the source of identity in integration tests.
                return Task.FromResult(AuthenticateResult.NoResult());
            }
        }
    }
}
