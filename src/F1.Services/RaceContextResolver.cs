using F1.Core.Dtos;
using F1.Core.Interfaces;

namespace F1.Services;

public sealed class RaceContextResolver(IRaceRepository raceRepository) : IRaceContextResolver
{
    public async Task<RaceContextResolutionDto?> ResolveByRoundAsync(string competitionSlug, int season, int round)
    {
        if (!TryNormalizeSlug(competitionSlug, out var normalizedCompetition))
        {
            throw new ArgumentException("Competition must use lower-case slug format (letters, numbers, hyphens).", nameof(competitionSlug));
        }

        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        if (round <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(round));
        }

        var race = await raceRepository.GetRaceByContextRoundAsync(normalizedCompetition, season, round);
        return race is null ? null : BuildResolution(race, normalizedCompetition);
    }

    public async Task<RaceContextResolutionDto?> ResolveBySlugAsync(string competitionSlug, int season, string raceSlug)
    {
        if (!TryNormalizeSlug(competitionSlug, out var normalizedCompetition))
        {
            throw new ArgumentException("Competition must use lower-case slug format (letters, numbers, hyphens).", nameof(competitionSlug));
        }

        if (!TryNormalizeSlug(raceSlug, out var normalizedRaceSlug))
        {
            throw new ArgumentException("Race slug must use lower-case slug format (letters, numbers, hyphens).", nameof(raceSlug));
        }

        if (season <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(season));
        }

        var race = await raceRepository.GetRaceByContextSlugAsync(normalizedCompetition, season, normalizedRaceSlug);
        return race is null ? null : BuildResolution(race, normalizedCompetition, normalizedRaceSlug);
    }

    private static RaceContextResolutionDto BuildResolution(F1.Core.Models.Race race, string competitionSlug, string? raceSlug = null)
    {
        return new RaceContextResolutionDto
        {
            RaceId = race.Id,
            CompetitionSlug = competitionSlug,
            Season = race.Season,
            Round = race.Round,
            RaceSlug = raceSlug ?? ExtractRaceSlug(race.Id)
        };
    }

    private static bool TryNormalizeSlug(string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        foreach (var ch in trimmed)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '-'))
            {
                return false;
            }
        }

        normalized = trimmed;
        return true;
    }

    private static string ExtractRaceSlug(string raceId)
    {
        if (string.IsNullOrWhiteSpace(raceId))
        {
            return raceId;
        }

        var parts = raceId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length - 2; i++)
        {
            if (parts[i].Length == 4
                && int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)
                && int.TryParse(parts[i + 1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return string.Join('-', parts[(i + 2)..]);
            }
        }

        return raceId;
    }
}
