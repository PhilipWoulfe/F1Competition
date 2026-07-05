using F1.Api.Controllers;
using F1.Core.Dtos;
using F1.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace F1.Api.Tests.Controllers;

public class RaceContextControllerTests
{
    [Fact]
    public async Task ResolveByRound_ShouldReturnOk_WhenContextMapsToRace()
    {
        var resolverMock = new Mock<IRaceContextResolver>();
        resolverMock
            .Setup(x => x.ResolveByRoundAsync("main", 2026, 2))
            .ReturnsAsync(new RaceContextResolutionDto
            {
                RaceId = "main-2026-2-australian-grand-prix",
                CompetitionSlug = "main",
                Season = 2026,
                Round = 2,
                RaceSlug = "australian-grand-prix"
            });

        var controller = new RaceContextController(resolverMock.Object);

        var result = await controller.ResolveByRound("main", 2026, 2);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<RaceContextResolutionDto>(ok.Value);
        Assert.Equal("main-2026-2-australian-grand-prix", payload.RaceId);
    }

    [Fact]
    public async Task ResolveByRound_ShouldReturnNotFound_WhenNoRaceMatches()
    {
        var resolverMock = new Mock<IRaceContextResolver>();
        resolverMock
            .Setup(x => x.ResolveByRoundAsync("main", 2026, 99))
            .ReturnsAsync((RaceContextResolutionDto?)null);

        var controller = new RaceContextController(resolverMock.Object);

        var result = await controller.ResolveByRound("main", 2026, 99);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ResolveBySlug_ShouldReturnNotFound_WhenNoRaceMatches()
    {
        var resolverMock = new Mock<IRaceContextResolver>();
        resolverMock
            .Setup(x => x.ResolveBySlugAsync("main", 2026, "unknown"))
            .ReturnsAsync((RaceContextResolutionDto?)null);

        var controller = new RaceContextController(resolverMock.Object);

        var result = await controller.ResolveBySlug("main", 2026, "unknown");

        Assert.IsType<NotFoundResult>(result);
    }
}
