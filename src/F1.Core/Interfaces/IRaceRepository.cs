using F1.Core.Models;

namespace F1.Core.Interfaces;

public interface IRaceRepository
{
    Task<Race?> GetRaceAsync(string raceId);
    Task<IReadOnlyList<Race>> GetRacesAsync();
    Task<Race?> GetRaceByContextRoundAsync(string competitionSlug, int season, int round);
    Task<Race?> GetRaceByContextSlugAsync(string competitionSlug, int season, string raceSlug);
}
