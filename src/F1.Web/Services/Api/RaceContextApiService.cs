using F1.Web.Models;
using System.Net;

namespace F1.Web.Services.Api;

public sealed class RaceContextApiService(HttpClient httpClient) : IRaceContextApiService
{
    public async Task<RaceContextResolution?> ResolveByRoundAsync(string competitionSlug, int season, int round, CancellationToken cancellationToken = default)
    {
        ValidateCompetitionAndSeason(competitionSlug, season);
        if (round <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(round));
        }

        using var response = await httpClient.GetAsync($"races/context/{competitionSlug}/{season}/round/{round}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponseParser.ReadOptionalJsonAsync<RaceContextResolution?>(response, null, "Resolving race context", cancellationToken);
    }

    public async Task<RaceContextResolution?> ResolveBySlugAsync(string competitionSlug, int season, string raceSlug, CancellationToken cancellationToken = default)
    {
        ValidateCompetitionAndSeason(competitionSlug, season);
        ArgumentException.ThrowIfNullOrWhiteSpace(raceSlug);

        using var response = await httpClient.GetAsync($"races/context/{competitionSlug}/{season}/slug/{raceSlug}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponseParser.ReadOptionalJsonAsync<RaceContextResolution?>(response, null, "Resolving race context", cancellationToken);
    }

    private static void ValidateCompetitionAndSeason(string competitionSlug, int season)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }
    }
}
