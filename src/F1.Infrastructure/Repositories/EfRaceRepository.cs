using F1.Core.Interfaces;
using F1.Core.Models;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Repositories;

public class EfRaceRepository : IRaceRepository
{
    private readonly F1DbContext _dbContext;

    public EfRaceRepository(F1DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Race?> GetRaceAsync(string raceId)
    {
        return await _dbContext.Races
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == raceId);
    }

    public async Task<IReadOnlyList<Race>> GetRacesAsync()
    {
        return await _dbContext.Races
            .AsNoTracking()
            .OrderBy(x => x.StartTimeUtc)
            .ToListAsync();
    }

    public async Task<Race?> GetRaceByContextRoundAsync(string competitionSlug, int season, int round)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        if (round <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(round));
        }

        var normalizedCompetition = competitionSlug.Trim().ToLowerInvariant();
        var prefix = $"{normalizedCompetition}-{season}-{round}-";

        return await _dbContext.Races
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Season == season && x.Round == round && x.Id.StartsWith(prefix));
    }

    public async Task<Race?> GetRaceByContextSlugAsync(string competitionSlug, int season, string raceSlug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(competitionSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(raceSlug);
        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        var normalizedCompetition = competitionSlug.Trim().ToLowerInvariant();
        var normalizedSlug = raceSlug.Trim().ToLowerInvariant();
        var seasonPrefix = $"{normalizedCompetition}-{season}-";
        var slugSuffix = $"-{normalizedSlug}";

        return await _dbContext.Races
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Season == season && x.Id.StartsWith(seasonPrefix) && x.Id.EndsWith(slugSuffix));
    }
}
