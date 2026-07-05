using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Services;
using Moq;

namespace F1.Api.Tests;

public class RaceContextResolverTests
{
    [Fact]
    public async Task ResolveByRoundAsync_ShouldReturnResolution_WhenRepositoryFindsRace()
    {
        var raceRepositoryMock = new Mock<IRaceRepository>();
        raceRepositoryMock
            .Setup(x => x.GetRaceByContextRoundAsync("main", 2026, 2))
            .ReturnsAsync(new Race
            {
                Id = "main-2026-2-australian-grand-prix",
                Season = 2026,
                Round = 2
            });

        var sut = new RaceContextResolver(raceRepositoryMock.Object);

        var result = await sut.ResolveByRoundAsync("main", 2026, 2);

        Assert.NotNull(result);
        Assert.Equal("main-2026-2-australian-grand-prix", result.RaceId);
        Assert.Equal("main", result.CompetitionSlug);
        Assert.Equal(2, result.Round);
        Assert.Equal("australian-grand-prix", result.RaceSlug);
    }

    [Fact]
    public async Task ResolveBySlugAsync_ShouldNormalizeInputs_ThenResolve()
    {
        var raceRepositoryMock = new Mock<IRaceRepository>();
        raceRepositoryMock
            .Setup(x => x.GetRaceByContextSlugAsync("main", 2026, "australian-grand-prix"))
            .ReturnsAsync(new Race
            {
                Id = "main-2026-2-australian-grand-prix",
                Season = 2026,
                Round = 2
            });

        var sut = new RaceContextResolver(raceRepositoryMock.Object);

        var result = await sut.ResolveBySlugAsync("MAIN", 2026, "Australian-Grand-Prix");

        Assert.NotNull(result);
        Assert.Equal("australian-grand-prix", result.RaceSlug);
    }
}
