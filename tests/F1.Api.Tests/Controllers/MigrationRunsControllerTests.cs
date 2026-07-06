using System.Security.Claims;
using F1.Api.Controllers;
using F1.Api.Dtos;
using F1.Api.Services;
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
    public async Task GetRuns_WhenNonAdmin_ReturnsForbid()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: false)
            }
        };

        var result = await controller.GetRuns();

        Assert.IsType<ForbidResult>(result.Result);
        service.Verify(x => x.GetRunsAsync(It.IsAny<MigrationRunListQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRunDetail_WhenAdminAndRunMissing_ReturnsNotFound()
    {
        var service = new Mock<IMigrationRunAdminService>();
        service.Setup(x => x.GetRunDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
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
    public async Task GetRunDetail_WhenNonAdmin_ReturnsForbid()
    {
        var service = new Mock<IMigrationRunAdminService>();
        var controller = new MigrationRunsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = CreateHttpContext(isAdmin: false)
            }
        };

        var result = await controller.GetRunDetail(Guid.NewGuid());

        Assert.IsType<ForbidResult>(result.Result);
        service.Verify(x => x.GetRunDetailAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
