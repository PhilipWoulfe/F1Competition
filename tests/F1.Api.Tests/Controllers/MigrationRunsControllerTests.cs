using System.Security.Claims;
using F1.Api.Controllers;
using F1.Api.Dtos;
using F1.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace F1.Api.Tests.Controllers;

public sealed class MigrationRunsControllerTests
{
    [Fact]
    public async Task GetRuns_WhenAdmin_ReturnsOkPayload()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IMigrationRunAdminService>();
        service.Setup(x => x.GetRunsAsync(
                It.IsAny<MigrationRunListQuery>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AdminMigrationRunListResponseDto(
                Page: 2,
                PageSize: 10,
                TotalCount: 1,
                Items:
                [
                    new AdminMigrationRunListItemDto(
                        RunId: runId,
                        Status: "Completed",
                        IsDryRun: true,
                        SourceFilePath: "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
                        SourceFileChecksum: "abc123",
                        StartedAtUtc: new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                        FinishedAtUtc: new DateTime(2026, 7, 6, 10, 3, 0, DateTimeKind.Utc),
                        RawRowCount: 250,
                        UnresolvedTokenCount: 2,
                        PickDiffCount: 120,
                        RaceDiffCount: 24,
                        TotalDeltaPoints: -9,
                        ErrorMessage: null)
                ]));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.GetRuns(page: 2, pageSize: 10, status: "Completed", startedFromUtc: null, startedToUtc: null);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AdminMigrationRunListResponseDto>(okResult.Value);
        Assert.Equal(2, payload.Page);
        Assert.Equal(10, payload.PageSize);
        Assert.Single(payload.Items);
        Assert.Equal(runId, payload.Items[0].RunId);

        service.Verify(x => x.GetRunsAsync(
            It.Is<MigrationRunListQuery>(query =>
                query.Page == 2 &&
                query.PageSize == 10 &&
                query.Status == "Completed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void MigrationRunsController_RequiresAdminRole()
    {
        var authorizeAttribute = typeof(MigrationRunsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        Assert.Equal("Admin", authorizeAttribute.Roles);
    }

    [Fact]
    public async Task GetRunDetail_WhenAdminAndRunMissing_ReturnsNotFound()
    {
        var service = new Mock<IMigrationRunAdminService>();
        service.Setup(x => x.GetRunDetailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdminMigrationRunDetailResponseDto?)null);

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.GetRunDetail(Guid.NewGuid());

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task GetRunDetail_WhenAdminAndRunExists_ReturnsOkPayload()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var runId = Guid.NewGuid();
        var detail = new AdminMigrationRunDetailResponseDto(
            RunId: runId,
            Status: "Completed",
            IsDryRun: false,
            SourceFilePath: "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
            SourceFileChecksum: "abc123",
            StartedAtUtc: new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
            FinishedAtUtc: new DateTime(2026, 7, 6, 10, 3, 0, DateTimeKind.Utc),
            RawRowCount: 250,
            ErrorMessage: null,
            UnresolvedTokenCount: 1,
            PickDiffCount: 2,
            RaceDiffCount: 3,
            TotalDeltaPoints: -4,
            UnresolvedTokenSummary: [],
            ParticipantDeltas: [],
            RaceDiffs: [],
            PickDiffs: []);

        service.Setup(x => x.GetRunDetailAsync(runId, "admin@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(detail);

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.GetRunDetail(runId);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AdminMigrationRunDetailResponseDto>(okResult.Value);
        Assert.Equal(runId, payload.RunId);
        service.Verify(x => x.GetRunDetailAsync(runId, "admin@example.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExportRunDiffs_WhenServiceReturnsPayload_ReturnsFileResult()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.ExportRunDiffsAsync(runId, "pick-diffs", "csv", "admin@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationRunDiffExportResponse(
                Success: true,
                Error: null,
                FileName: $"migration-run-{runId}-pick-diffs.csv",
                ContentType: "text/csv",
                Payload: [1, 2, 3]));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.ExportRunDiffs(runId, "pick-diffs", "csv");

        var fileResult = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", fileResult.ContentType);
        Assert.Equal($"migration-run-{runId}-pick-diffs.csv", fileResult.FileDownloadName);
    }

    [Fact]
    public async Task ExportRunDiffs_WhenRequestInvalid_ReturnsBadRequest()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.ExportRunDiffsAsync(runId, "bad-export", "xml", "admin@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationRunDiffExportResponse(
                Success: false,
                Error: "format must be either csv or json.",
                FileName: string.Empty,
                ContentType: "text/plain",
                Payload: []));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.ExportRunDiffs(runId, "bad-export", "xml");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var details = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("format must be either csv or json.", details.Detail);
    }

    [Fact]
    public async Task ExportRunDiffs_WhenRunMissing_ReturnsNotFound()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.ExportRunDiffsAsync(runId, "pick-diffs", "csv", "admin@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((MigrationRunDiffExportResponse?)null);

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.ExportRunDiffs(runId, "pick-diffs", "csv");

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task KickoffRun_WhenValidRequest_ReturnsCreatedPayload()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IMigrationRunAdminService>();
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
                    RunId: runId,
                    Status: "Started",
                    IsDryRun: true,
                    RequestedMode: "dry-run",
                    SourceFilePath: "/tmp/import.csv",
                    SourceFileChecksum: "abc123",
                    TriggeredAtUtc: new DateTime(2026, 7, 6, 13, 0, 0, DateTimeKind.Utc),
                    RequestedBy: "admin@example.com")));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.KickoffRun(new AdminMigrationRunKickoffRequestDto(
            SourceFilePath: "/tmp/import.csv",
            Mode: "dry-run"));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var payload = Assert.IsType<AdminMigrationRunKickoffResponseDto>(created.Value);
        Assert.Equal(runId, payload.RunId);
        Assert.Equal(nameof(MigrationRunsController.GetRunDetail), created.ActionName);
    }

    [Fact]
    public async Task KickoffRun_WhenActiveDuplicateExists_ReturnsConflict()
    {
        var existingRunId = Guid.NewGuid();
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.KickoffRunAsync(
                It.IsAny<MigrationRunKickoffCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationRunKickoffResult(
                Success: false,
                Conflict: true,
                Error: "An active migration run already exists for this source/checksum.",
                ExistingRunId: existingRunId,
                Run: null));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.KickoffRun(new AdminMigrationRunKickoffRequestDto(
            SourceFilePath: "/tmp/import.csv",
            Mode: "dry-run"));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
    }

    [Fact]
    public async Task KickoffRun_WhenRequestInvalid_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.KickoffRunAsync(
                It.IsAny<MigrationRunKickoffCommand>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationRunKickoffResult(
                Success: false,
                Conflict: false,
                Error: "Mode is required.",
                ExistingRunId: null,
                Run: null));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.KickoffRun(new AdminMigrationRunKickoffRequestDto(
            SourceFilePath: "/tmp/import.csv",
            Mode: string.Empty));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    private static HttpContext CreateHttpContext(bool isAdmin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "admin@example.com")
        };

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        }

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
    }
}
