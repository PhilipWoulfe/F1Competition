using F1.Api.Controllers;
using F1.Core.Dtos;
using F1.Core.Interfaces;
using F1.Core.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace F1.Api.Tests.Controllers;

public class RaceMetadataControllerTests
{
    private const string RaceId = "main-2025-24-abu-dhabi-grand-prix";
    private const string UnknownCanonicalRaceId = "main-2025-99-no-such-race";
    private const string NonCanonicalRaceId = "2025-24-abu_dhabi";

    [Fact]
    public async Task GetMetadata_ShouldReturnNotFound_WhenNoPublishedMetadataExists()
    {
        var serviceMock = new Mock<IRaceMetadataService>();
        serviceMock.Setup(service => service.GetMetadataAsync(UnknownCanonicalRaceId, true)).ReturnsAsync((RaceQuestionMetadata?)null);

        var controller = CreateController(serviceMock);

        var result = await controller.GetMetadata(UnknownCanonicalRaceId);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetMetadata_ShouldReturnForbid_WhenIncludeDraftRequestedByNonAdmin()
    {
        var serviceMock = new Mock<IRaceMetadataService>();
        var controller = CreateController(serviceMock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "user@example.com")], "TestAuth"))
            }
        };

        var result = await controller.GetMetadata(RaceId, includeDraft: true);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [InlineData(NonCanonicalRaceId)]
    [InlineData("MAIN-2025-24-abu-dhabi-grand-prix")]
    public async Task Endpoints_ShouldReturnBadRequest_WhenRaceIdIsNonCanonical(string raceId)
    {
        var serviceMock = new Mock<IRaceMetadataService>(MockBehavior.Strict);
        var controller = CreateController(serviceMock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildAdminContext()
        };

        Assert.IsType<BadRequestObjectResult>(await controller.GetMetadata(raceId));
        Assert.IsType<BadRequestObjectResult>(await controller.UpsertMetadata(raceId, new UpsertRaceQuestionMetadataDto
        {
            H2HQuestion = "Who wins head-to-head?",
            BonusQuestion = "Who gets fastest lap?",
            IsPublished = true
        }));

        serviceMock.VerifyNoOtherCalls();
    }

    private static RaceMetadataController CreateController(Mock<IRaceMetadataService> serviceMock)
    {
        return new RaceMetadataController(serviceMock.Object);
    }

    private static HttpContext BuildAdminContext()
    {
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Email, "admin@example.com"),
                new Claim(ClaimTypes.Role, "Admin")
            ], "TestAuth"))
        };
    }
}
