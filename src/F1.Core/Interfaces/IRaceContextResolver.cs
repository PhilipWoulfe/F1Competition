using F1.Core.Dtos;

namespace F1.Core.Interfaces;

public interface IRaceContextResolver
{
    Task<RaceContextResolutionDto?> ResolveByRoundAsync(string competitionSlug, int season, int round);
    Task<RaceContextResolutionDto?> ResolveBySlugAsync(string competitionSlug, int season, string raceSlug);
}
