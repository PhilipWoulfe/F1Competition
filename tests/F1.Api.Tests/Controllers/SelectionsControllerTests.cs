using F1.Api.Controllers;
using F1.Core.Dtos;
using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Security.Claims;

namespace F1.Api.Tests.Controllers;

public class SelectionsControllerTests
{
    private const string RaceId = "main-2026-2-australian-grand-prix";
    private const string UnknownCanonicalRaceId = "main-2026-99-no-such-race";
    private const string NonCanonicalRaceId = "2026-01-albert_park";
    private static readonly ISelectionRuleProvider SelectionRuleProvider = new SelectionRuleProvider();

    [Fact]
    public async Task GetCurrent_ShouldReturnUnauthorized_WhenUserCannotBeResolved()
    {
        var serviceMock = new Mock<ISelectionService>();
        var controller = CreateController(serviceMock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.GetCurrent(RaceId);

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetCurrent_ShouldReturnOk_WithSelectionRows()
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

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@example.com")],
            "TestAuth"));

        var controller = CreateController(serviceMock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var result = await controller.GetCurrent(RaceId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<CurrentSelectionDto>>(ok.Value);
        Assert.Single(payload);
        Assert.Equal(1, payload[0].Position);
        Assert.Equal("norris", payload[0].DriverId);
    }

    [Fact]
    public async Task GetCurrent_ShouldReturnMockRows_InDevelopmentWhenEnabled()
    {
        const string userId = "mock-current@example.com";
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, userId)],
            "TestAuth"));

        var service = BuildSelectionServiceWithMockCurrentSelections();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);
        var controller = new SelectionsController(service, dateTimeProvider.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        await controller.UpsertMine(RaceId, new SelectionSubmissionDto
        {
            BetType = F1.Core.Models.BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "max_verstappen" },
                new SelectionPosition { Position = 2, DriverId = "lando_norris" },
                new SelectionPosition { Position = 3, DriverId = "charles_leclerc" },
                new SelectionPosition { Position = 4, DriverId = "oscar_piastri" },
                new SelectionPosition { Position = 5, DriverId = "lewis_hamilton" }
            }
        });

        var result = await controller.GetCurrent(RaceId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsAssignableFrom<IReadOnlyList<CurrentSelectionDto>>(ok.Value);
        Assert.NotEmpty(payload);
        Assert.Equal(1, payload[0].Position);
        Assert.Equal("max_verstappen", payload[0].DriverId);
    }

    [Fact]
    public async Task GetCurrent_ShouldPassRouteRaceIdToService()
    {
        var serviceMock = new Mock<ISelectionService>();
        serviceMock
            .Setup(service => service.GetCurrentSelectionsAsync(RaceId, "user@example.com"))
            .ReturnsAsync(Array.Empty<CurrentSelectionDto>());

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@example.com")],
            "TestAuth"));

        var controller = CreateController(serviceMock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var result = await controller.GetCurrent(RaceId);

        Assert.IsType<OkObjectResult>(result);
        serviceMock.Verify(service => service.GetCurrentSelectionsAsync(RaceId, "user@example.com"), Times.Once);
    }

    [Fact]
    public async Task GetMine_ShouldReturnPersistedMockSelection_InDevelopmentWhenEnabled()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@example.com")],
            "TestAuth"));

        var service = BuildSelectionServiceWithMockCurrentSelections();
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);
        var controller = new SelectionsController(service, dateTimeProvider.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        await controller.UpsertMine(RaceId, new SelectionSubmissionDto
        {
            BetType = F1.Core.Models.BetType.PreQualy,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        });

        var result = await controller.GetMine(RaceId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<F1.Core.Models.Selection>(ok.Value);
        Assert.Equal(F1.Core.Models.BetType.PreQualy, payload.BetType);
        Assert.Equal("norris", payload.OrderedSelections[0].DriverId);
    }

    [Fact]
    public async Task UpsertMine_ShouldReturnNotFound_WhenRaceDoesNotExist()
    {
        var serviceMock = new Mock<ISelectionService>();
        serviceMock
            .Setup(service => service.UpsertSelectionAsync(UnknownCanonicalRaceId, "user@example.com", It.IsAny<SelectionSubmissionDto>()))
            .ThrowsAsync(new SelectionRaceNotFoundException($"Race '{UnknownCanonicalRaceId}' not found."));

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@example.com")],
            "TestAuth"));

        var controller = CreateController(serviceMock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        var result = await controller.UpsertMine(UnknownCanonicalRaceId, new SelectionSubmissionDto
        {
            BetType = F1.Core.Models.BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        });

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Theory]
    [InlineData(NonCanonicalRaceId)]
    [InlineData("MAIN-2026-2-australian-grand-prix")]
    public async Task Endpoints_ShouldReturnBadRequest_WhenRaceIdIsNonCanonical(string raceId)
    {
        var serviceMock = new Mock<ISelectionService>(MockBehavior.Strict);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "user@example.com")],
            "TestAuth"));

        var controller = CreateController(serviceMock);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        Assert.IsType<BadRequestObjectResult>(await controller.GetConfig(raceId));
        Assert.IsType<BadRequestObjectResult>(await controller.GetMine(raceId));
        Assert.IsType<BadRequestObjectResult>(await controller.GetCurrent(raceId));
        Assert.IsType<BadRequestObjectResult>(await controller.UpsertMine(raceId, new SelectionSubmissionDto
        {
            BetType = F1.Core.Models.BetType.Regular,
            OrderedSelections = new List<SelectionPosition>
            {
                new SelectionPosition { Position = 1, DriverId = "norris" },
                new SelectionPosition { Position = 2, DriverId = "leclerc" },
                new SelectionPosition { Position = 3, DriverId = "hamilton" },
                new SelectionPosition { Position = 4, DriverId = "piastri" },
                new SelectionPosition { Position = 5, DriverId = "verstappen" }
            }
        }));

        serviceMock.VerifyNoOtherCalls();
    }

    public ISelectionService BuildSelectionServiceWithMockCurrentSelections()
    {
        var mockRepo = new Mock<ISelectionRepository>();
        var mockDriverRepo = new Mock<IDriverRepository>();
        var mockRaceRepo = new Mock<IRaceRepository>();
        var mockDateTimeProvider = new Mock<IDateTimeProvider>();
        var store = new Dictionary<string, Selection>(StringComparer.OrdinalIgnoreCase);

        mockRepo
            .Setup(repo => repo.GetSelectionAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync((string raceId, string userId) =>
            {
                store.TryGetValue($"{raceId}::{userId}", out var selection);
                return selection;
            });

        mockRepo
            .Setup(repo => repo.UpsertSelectionAsync(It.IsAny<Selection>()))
            .ReturnsAsync((Selection selection) =>
            {
                if (selection.Id == Guid.Empty)
                {
                    selection.Id = Guid.NewGuid();
                }

                store[$"{selection.RaceId}::{selection.UserId}"] = selection;
                return selection;
            });

        mockDriverRepo.Setup(repo => repo.GetDriversAsync()).ReturnsAsync(new List<Driver>
        {
            new Driver { DriverId = "max_verstappen", FullName = "Max Verstappen" },
            new Driver { DriverId = "lando_norris", FullName = "Lando Norris" },
            new Driver { DriverId = "charles_leclerc", FullName = "Charles Leclerc" },
            new Driver { DriverId = "oscar_piastri", FullName = "Oscar Piastri" },
            new Driver { DriverId = "lewis_hamilton", FullName = "Lewis Hamilton" },
            new Driver { DriverId = "norris", FullName = "Lando Norris" },
            new Driver { DriverId = "leclerc", FullName = "Charles Leclerc" },
            new Driver { DriverId = "hamilton", FullName = "Lewis Hamilton" },
            new Driver { DriverId = "piastri", FullName = "Oscar Piastri" },
            new Driver { DriverId = "verstappen", FullName = "Max Verstappen" }
        });

        mockRaceRepo
            .Setup(repo => repo.GetRaceAsync(It.IsAny<string>()))
            .ReturnsAsync((string raceId) => new Race
            {
                Id = raceId,
                Season = 2026,
                PreQualyDeadlineUtc = new DateTime(2026, 3, 15, 4, 0, 0, DateTimeKind.Utc),
                FinalDeadlineUtc = new DateTime(2026, 3, 15, 6, 0, 0, DateTimeKind.Utc)
            });

        return new SelectionService(mockRepo.Object, mockDriverRepo.Object, mockRaceRepo.Object, mockDateTimeProvider.Object, SelectionRuleProvider);
    }

    private static SelectionsController CreateController(Mock<ISelectionService> serviceMock)
    {
        var dateTimeProvider = new Mock<IDateTimeProvider>();
        dateTimeProvider.SetupGet(x => x.UtcNow).Returns(DateTime.UtcNow);

        return new SelectionsController(serviceMock.Object, dateTimeProvider.Object);
    }
}
