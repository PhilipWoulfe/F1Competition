using System.Security.Claims;
using F1.Api.Controllers;
using F1.Api.Dtos;
using F1.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.IO.Compression;

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
                        UnexpectedTotalDeltaPoints: -7,
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
        service.Setup(x => x.GetRunDetailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "all"))
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
            UnexpectedTotalDeltaPoints: -4,
            UnresolvedTokenSummary: [],
            ParticipantDeltas: [],
            PreseasonSummary: new AdminMigrationPreseasonSummaryDto(0, 0, 0, 0),
            PreseasonParticipantDeltas: [],
            PreseasonQuestionDiffs: [],
            PreseasonReasonCategorySummaries: [],
            RaceDiffs: [],
            PickDiffs: []);

        service.Setup(x => x.GetRunDetailAsync(runId, "admin@example.com", It.IsAny<CancellationToken>(), "all"))
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
        service.Verify(x => x.GetRunDetailAsync(runId, "admin@example.com", It.IsAny<CancellationToken>(), "all"), Times.Once);
    }

    [Fact]
    public async Task GetRunDetail_WhenExpectedStatusRequested_ForwardsFilterToService()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var runId = Guid.NewGuid();
        service.Setup(x => x.GetRunDetailAsync(runId, "admin@example.com", It.IsAny<CancellationToken>(), "unexpected"))
            .ReturnsAsync(new AdminMigrationRunDetailResponseDto(
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
                PickDiffCount: 1,
                RaceDiffCount: 1,
                TotalDeltaPoints: 5,
                UnexpectedTotalDeltaPoints: 5,
                UnresolvedTokenSummary: [],
                ParticipantDeltas: [],
                PreseasonSummary: new AdminMigrationPreseasonSummaryDto(0, 0, 0, 0),
                PreseasonParticipantDeltas: [],
                PreseasonQuestionDiffs: [],
                PreseasonReasonCategorySummaries: [],
                RaceDiffs: [],
                PickDiffs: []));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.GetRunDetail(runId, expectedStatus: "unexpected");

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AdminMigrationRunDetailResponseDto>(okResult.Value);
        Assert.Equal(runId, payload.RunId);
        service.Verify(x => x.GetRunDetailAsync(runId, "admin@example.com", It.IsAny<CancellationToken>(), "unexpected"), Times.Once);
    }

    [Fact]
    public async Task GetRunDetail_WhenExpectedStatusInvalid_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.GetRunDetail(Guid.NewGuid(), expectedStatus: "oops");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var details = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Invalid expected status filter", details.Title);
        Assert.Equal("expectedStatus must be one of: all, expected, unexpected.", details.Detail);
        service.Verify(x => x.GetRunDetailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task GetQuestionDiffs_ForwardsFiltersToService()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var runId = Guid.NewGuid();

        service.Setup(x => x.GetQuestionDiffsAsync(
                runId,
                2,
                10,
                "admin@example.com",
                It.IsAny<CancellationToken>(),
                "Preseason",
                "phil",
                "unexpected",
                true))
            .ReturnsAsync(new AdminMigrationQuestionDiffListResponseDto(
                Page: 2,
                PageSize: 10,
                TotalCount: 1,
                Items:
                [
                    new AdminMigrationQuestionDiffDto(
                        Category: "Preseason",
                        QuestionId: "PRE-001",
                        QuestionText: "Question",
                        Participant: "Philip",
                        ImportedPoints: 20,
                        CalculatedPoints: 0,
                        DeltaPoints: -20)
                ]));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.GetQuestionDiffs(
            runId,
            page: 2,
            pageSize: 10,
            category: "Preseason",
            participant: "phil",
            expectedStatus: "unexpected",
            nonZeroDeltaOnly: true);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<AdminMigrationQuestionDiffListResponseDto>(okResult.Value);
        Assert.Equal(2, payload.Page);
        Assert.Single(payload.Items);
    }

    [Fact]
    public async Task GetQuestionSummary_WhenExpectedStatusInvalid_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.GetQuestionSummary(Guid.NewGuid(), expectedStatus: "oops");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var details = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Invalid expected status filter", details.Title);
    }

    [Fact]
    public async Task ExportRunDiffs_WhenServiceReturnsPayload_ReturnsFileResult()
    {
        var runId = Guid.NewGuid();
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.ExportRunDiffsAsync(runId, "pick-diffs", "csv", "admin@example.com", It.IsAny<CancellationToken>(), "all", null, null, false))
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
            .Setup(x => x.ExportRunDiffsAsync(runId, "bad-export", "xml", "admin@example.com", It.IsAny<CancellationToken>(), "all", null, null, false))
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
            .Setup(x => x.ExportRunDiffsAsync(runId, "pick-diffs", "csv", "admin@example.com", It.IsAny<CancellationToken>(), "all", null, null, false))
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
    public async Task ExportRunDiffs_WhenExpectedStatusInvalid_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.ExportRunDiffs(Guid.NewGuid(), "pick-diffs", "csv", expectedStatus: "oops");

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var details = Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal("Invalid expected status filter", details.Title);
        Assert.Equal("expectedStatus must be one of: all, expected, unexpected.", details.Detail);
        service.Verify(x => x.ExportRunDiffsAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>()), Times.Never);
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

    [Fact]
    public async Task KickoffRunFromUpload_WhenValidCsv_ReturnsCreatedPayload()
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
                    SourceFilePath: "data/imports/uploads/upload.csv",
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

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Question,Philip\nAUS-1,VER"));
        var formFile = new FormFile(stream, 0, stream.Length, "SourceFile", "import.csv");

        var result = await controller.KickoffRunFromUpload(new AdminMigrationRunKickoffUploadRequestDto(
            SourceFile: formFile,
            Mode: "dry-run"));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var payload = Assert.IsType<AdminMigrationRunKickoffResponseDto>(created.Value);
        Assert.Equal(runId, payload.RunId);

        service.Verify(x => x.KickoffRunAsync(
            It.Is<MigrationRunKickoffCommand>(command =>
                command.RequestedMode == "dry-run" &&
                command.SourceFilePath != null &&
                command.SourceFilePath.Contains("data/imports/uploads")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KickoffRunFromUpload_WhenFileMissing_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.KickoffRunFromUpload(new AdminMigrationRunKickoffUploadRequestDto(
            SourceFile: null,
            Mode: "dry-run"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
    }

    [Fact]
    public async Task KickoffRunFromUpload_WhenDaveProfileWithValidZip_ReturnsCreatedPayload()
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
                    SourceFilePath: "data/imports/uploads/dave-package",
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

        await using var archiveStream = BuildDaveZipArchive(
            ("races.csv", "Name,Race1-1\nAlice,NOR"),
            ("bonus.csv", "Question,Alice\nQ1,Yes"),
            ("bonusAnswers.csv", "Question,Answer\nQ1,Yes"),
            ("Leaderboard.csv", "Name,Total\nAlice,100"));
        var formFile = new FormFile(archiveStream, 0, archiveStream.Length, "SourceFile", "dave-package.zip");

        var result = await controller.KickoffRunFromUpload(new AdminMigrationRunKickoffUploadRequestDto(
            SourceFile: formFile,
            Mode: "dry-run",
            SourceProfile: "dave-2025-package"));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var payload = Assert.IsType<AdminMigrationRunKickoffResponseDto>(created.Value);
        Assert.Equal(runId, payload.RunId);

        service.Verify(x => x.KickoffRunAsync(
            It.Is<MigrationRunKickoffCommand>(command =>
                command.RequestedMode == "dry-run" &&
                command.SourceProfile == "dave-2025-package" &&
                command.SourceFilePath != null &&
                command.SourceFilePath.Contains("uploads")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KickoffRunFromUpload_WhenDaveProfileWithLowercaseLeaderboardFile_ReturnsCreatedPayload()
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
                    SourceFilePath: "data/imports/uploads/dave-package",
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

        await using var archiveStream = BuildDaveZipArchive(
            ("races.csv", "Name,Race1-1\nAlice,NOR"),
            ("bonus.csv", "Question,Alice\nQ1,Yes"),
            ("bonusAnswers.csv", "Question,Answer\nQ1,Yes"),
            ("leaderboard.csv", "Name,Total\nAlice,100"));
        var formFile = new FormFile(archiveStream, 0, archiveStream.Length, "SourceFile", "dave-package.zip");

        var result = await controller.KickoffRunFromUpload(new AdminMigrationRunKickoffUploadRequestDto(
            SourceFile: formFile,
            Mode: "dry-run",
            SourceProfile: "dave-2025-package"));

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var payload = Assert.IsType<AdminMigrationRunKickoffResponseDto>(created.Value);
        Assert.Equal(runId, payload.RunId);

        service.Verify(x => x.KickoffRunAsync(
            It.Is<MigrationRunKickoffCommand>(command =>
                command.SourceProfile == "dave-2025-package" &&
                command.SourceFilePath != null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task KickoffRunFromUpload_WhenDaveProfileWithCsv_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Question,Philip\nAUS-1,VER"));
        var formFile = new FormFile(stream, 0, stream.Length, "SourceFile", "import.csv");

        var result = await controller.KickoffRunFromUpload(new AdminMigrationRunKickoffUploadRequestDto(
            SourceFile: formFile,
            Mode: "dry-run",
            SourceProfile: "dave-2025-package"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        service.Verify(x => x.KickoffRunAsync(It.IsAny<MigrationRunKickoffCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task KickoffRunFromUpload_WhenDaveArchiveContainsTraversal_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        await using var archiveStream = BuildDaveZipArchive(
            ("../races.csv", "Name,Race1-1\nAlice,NOR"),
            ("bonus.csv", "Question,Alice\nQ1,Yes"),
            ("bonusAnswers.csv", "Question,Answer\nQ1,Yes"),
            ("Leaderboard.csv", "Name,Total\nAlice,100"));
        var formFile = new FormFile(archiveStream, 0, archiveStream.Length, "SourceFile", "dave-package.zip");

        var result = await controller.KickoffRunFromUpload(new AdminMigrationRunKickoffUploadRequestDto(
            SourceFile: formFile,
            Mode: "dry-run",
            SourceProfile: "dave-2025-package"));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        service.Verify(x => x.KickoffRunAsync(It.IsAny<MigrationRunKickoffCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RollbackRun_WhenReasonMissing_ReturnsBadRequest()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.RollbackRun(Guid.NewGuid(), new AdminMigrationRollbackRequestDto("  "));

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        service.Verify(x => x.RollbackRunAsync(It.IsAny<MigrationRunRollbackCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RollbackRun_WhenServiceSucceeds_ReturnsOkPayload()
    {
        var runId = Guid.NewGuid();
        var requestedAtUtc = new DateTime(2026, 7, 7, 8, 0, 0, DateTimeKind.Utc);
        var service = new Mock<IMigrationRunAdminService>();
        service
            .Setup(x => x.RollbackRunAsync(It.IsAny<MigrationRunRollbackCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MigrationRunRollbackResult(
                Success: true,
                Error: null,
                Rollback: new AdminMigrationRollbackResponseDto(
                    RunId: runId,
                    Status: "RolledBack",
                    RequestedAtUtc: requestedAtUtc,
                    RequestedBy: "admin@example.com",
                    Outcome: "Completed",
                    AffectedRaceCount: 1,
                    AffectedSelectionCount: 2,
                    AffectedSelectionPositionCount: 6)));

        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: true)
            }
        };

        var result = await controller.RollbackRun(runId, new AdminMigrationRollbackRequestDto("bad canonical write"));

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<AdminMigrationRollbackResponseDto>(ok.Value);
        Assert.Equal(runId, payload.RunId);
        Assert.Equal("RolledBack", payload.Status);
        service.Verify(x => x.RollbackRunAsync(
            It.Is<MigrationRunRollbackCommand>(command =>
                command.RunId == runId &&
                command.RequestedBy == "admin@example.com" &&
                command.Reason == "bad canonical write"),
            It.IsAny<CancellationToken>()), Times.Once);
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

    private static MemoryStream BuildDaveZipArchive(params (string FileName, string Content)[] entries)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (fileName, content) in entries)
            {
                var entry = archive.CreateEntry(fileName);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }
}
