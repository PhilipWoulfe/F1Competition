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
using System.Net.Http.Json;
using System.Text.Encodings.Web;

namespace F1.Api.Tests.Integration;

public sealed class AdminMigrationRunsRouteAccessIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestConnectionString = "Host=localhost;Port=5432;Database=f1_tests;Username=f1;Password=f1";

    private readonly WebApplicationFactory<Program> _factory;

    public AdminMigrationRunsRouteAccessIntegrationTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", TestConnectionString);
        _factory = factory;
    }

    [Fact]
    public async Task MigrationRunsRoute_WhenAnonymous_ShouldReturnUnauthorized()
    {
        var client = CreateClient(mockEmail: null, mockGroups: null);

        var response = await client.GetAsync("/admin/migration-runs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunsRoute_WhenAuthenticatedNonAdmin_ShouldReturnForbidden()
    {
        var client = CreateClient(mockEmail: "user@example.com", mockGroups: ["F1 Users"]);

        var response = await client.GetAsync("/admin/migration-runs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunsRoute_WhenAuthenticatedAdmin_ShouldReturnOk()
    {
        var client = CreateClient(mockEmail: "admin@example.com", mockGroups: ["F1 Admins"]);

        var response = await client.GetAsync("/admin/migration-runs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private HttpClient CreateClient(string? mockEmail, string[]? mockGroups)
    {
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.GetRunsAsync(It.IsAny<MigrationRunListQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminMigrationRunListResponseDto(1, 25, 0, []));
        service
            .Setup(x => x.ExportRunDiffsAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((MigrationRunDiffExportResponse?)null);
        service
            .Setup(x => x.KickoffRunAsync(
                It.IsAny<MigrationRunKickoffCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationRunKickoffResult(
                Success: true,
                Conflict: false,
                Error: null,
                ExistingRunId: null,
                Run: new AdminMigrationRunKickoffResponseDto(
                    RunId: Guid.NewGuid(),
                    Status: "Started",
                    IsDryRun: true,
                    RequestedMode: "dry-run",
                    SourceFilePath: "/tmp/import.csv",
                    SourceFileChecksum: "abc123",
                    TriggeredAtUtc: DateTime.UtcNow,
                    RequestedBy: "admin@example.com")));

        return _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var values = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Postgres"] = TestConnectionString,
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

                services.RemoveAll<IMigrationRunAdminService>();
                services.AddScoped(_ => service.Object);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task MigrationRunExportRoute_WhenAnonymous_ShouldReturnUnauthorized()
    {
        var client = CreateClient(mockEmail: null, mockGroups: null);

        var response = await client.GetAsync($"/admin/migration-runs/{Guid.NewGuid()}/exports/pick-diffs?format=csv");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunExportRoute_WhenAuthenticatedNonAdmin_ShouldReturnForbidden()
    {
        var client = CreateClient(mockEmail: "user@example.com", mockGroups: ["F1 Users"]);

        var response = await client.GetAsync($"/admin/migration-runs/{Guid.NewGuid()}/exports/pick-diffs?format=csv");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunExportRoute_WhenAuthenticatedAdminAndRunMissing_ShouldReturnNotFound()
    {
        var client = CreateClient(mockEmail: "admin@example.com", mockGroups: ["F1 Admins"]);

        var response = await client.GetAsync($"/admin/migration-runs/{Guid.NewGuid()}/exports/pick-diffs?format=csv");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunKickoffRoute_WhenAnonymous_ShouldReturnUnauthorized()
    {
        var client = CreateClient(mockEmail: null, mockGroups: null);

        var response = await client.PostAsJsonAsync("/admin/migration-runs/kickoff", new { sourceFilePath = "/tmp/import.csv", mode = "dry-run" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunKickoffRoute_WhenAuthenticatedNonAdmin_ShouldReturnForbidden()
    {
        var client = CreateClient(mockEmail: "user@example.com", mockGroups: ["F1 Users"]);

        var response = await client.PostAsJsonAsync("/admin/migration-runs/kickoff", new { sourceFilePath = "/tmp/import.csv", mode = "dry-run" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunKickoffRoute_WhenAuthenticatedAdmin_ShouldReturnCreated()
    {
        var client = CreateClient(mockEmail: "admin@example.com", mockGroups: ["F1 Admins"]);

        var response = await client.PostAsJsonAsync("/admin/migration-runs/kickoff", new { sourceFilePath = "/tmp/import.csv", mode = "dry-run" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunKickoffUploadRoute_WhenAnonymous_ShouldReturnUnauthorized()
    {
        var client = CreateClient(mockEmail: null, mockGroups: null);

        using var content = CreateUploadContent();
        var response = await client.PostAsync("/admin/migration-runs/kickoff/upload", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunKickoffUploadRoute_WhenAuthenticatedNonAdmin_ShouldReturnForbidden()
    {
        var client = CreateClient(mockEmail: "user@example.com", mockGroups: ["F1 Users"]);

        using var content = CreateUploadContent();
        var response = await client.PostAsync("/admin/migration-runs/kickoff/upload", content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MigrationRunKickoffUploadRoute_WhenAuthenticatedAdmin_ShouldReturnCreated()
    {
        var client = CreateClient(mockEmail: "admin@example.com", mockGroups: ["F1 Admins"]);

        using var content = CreateUploadContent();
        var response = await client.PostAsync("/admin/migration-runs/kickoff/upload", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static MultipartFormDataContent CreateUploadContent()
    {
        var content = new MultipartFormDataContent();
        var csvBytes = System.Text.Encoding.UTF8.GetBytes("Question,Philip\nAUS-1,VER");
        content.Add(new ByteArrayContent(csvBytes), "SourceFile", "import.csv");
        content.Add(new StringContent("dry-run"), "Mode");
        return content;
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
