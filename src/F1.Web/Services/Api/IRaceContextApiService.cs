using F1.Web.Models;

namespace F1.Web.Services.Api;

public interface IRaceContextApiService
{
    Task<RaceContextResolution?> ResolveByRoundAsync(string competitionSlug, int season, int round, CancellationToken cancellationToken = default);
    Task<RaceContextResolution?> ResolveBySlugAsync(string competitionSlug, int season, string raceSlug, CancellationToken cancellationToken = default);
}
