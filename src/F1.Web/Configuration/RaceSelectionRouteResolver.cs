using F1.Web.Models;

namespace F1.Web.Configuration;

public static class RaceSelectionRouteResolver
{
    public static bool TryResolve(
        string? raceIdFromRoute,
        string? competitionFromRoute,
        int? seasonFromRoute,
        int? roundFromRoute,
        string? raceSlugFromRoute,
        string relativePath,
        out RaceSelectionContext? context,
        out string? errorMessage)
    {
        context = null;
        errorMessage = null;

        if (TryNormalizeRaceId(raceIdFromRoute, out var normalizedRaceId))
        {
            context = new RaceSelectionContext
            {
                RaceId = normalizedRaceId,
                ContextKey = $"raceId:{normalizedRaceId}"
            };
            return true;
        }

        if (!string.IsNullOrWhiteSpace(raceIdFromRoute))
        {
            errorMessage = "Race context is invalid. Only letters, numbers, underscores, and hyphens are allowed.";
            return false;
        }

        if (TryBuildLookup(competitionFromRoute, seasonFromRoute, roundFromRoute, raceSlugFromRoute, out var lookup, out errorMessage)
            && lookup is not null)
        {
            context = new RaceSelectionContext
            {
                Lookup = lookup,
                ContextKey = lookup.LookupType == RaceRouteLookupType.Round
                    ? $"ctx:{lookup.CompetitionSlug}:{lookup.Season}:round:{lookup.LookupValue}"
                    : $"ctx:{lookup.CompetitionSlug}:{lookup.Season}:slug:{lookup.LookupValue}"
            };
            return true;
        }

        if (IsCompatibilityRoute(relativePath))
        {
            // Temporary compatibility route. Remove in PR F cleanup.
            context = new RaceSelectionContext
            {
                RaceId = SelectionDefaults.CompatibilityRaceId,
                ContextKey = $"compat:{SelectionDefaults.CompatibilityRoutePath}"
            };
            return true;
        }

        errorMessage ??= "Race context is missing. Open this page using /selection/{raceId} or /selection/{competition}/{season}/round/{round}.";
        return false;
    }

    private static bool TryBuildLookup(
        string? competitionFromRoute,
        int? seasonFromRoute,
        int? roundFromRoute,
        string? raceSlugFromRoute,
        out RaceRouteLookup? lookup,
        out string? errorMessage)
    {
        lookup = null;
        errorMessage = null;

        var hasCompetition = !string.IsNullOrWhiteSpace(competitionFromRoute);
        var hasSeason = seasonFromRoute.HasValue;
        var hasRound = roundFromRoute.HasValue;
        var hasSlug = !string.IsNullOrWhiteSpace(raceSlugFromRoute);

        if (!hasCompetition && !hasSeason && !hasRound && !hasSlug)
        {
            return false;
        }

        if (!TryNormalizeSlug(competitionFromRoute, out var competitionSlug))
        {
            errorMessage = "Race context is invalid. Competition must be a slug with letters, numbers, and hyphens.";
            return false;
        }

        if (!seasonFromRoute.HasValue || seasonFromRoute.Value <= 0)
        {
            errorMessage = "Race context is invalid. Season must be a positive number.";
            return false;
        }

        if (hasRound && hasSlug)
        {
            errorMessage = "Race context is invalid. Provide either round or race slug, not both.";
            return false;
        }

        if (hasRound)
        {
            var round = roundFromRoute.GetValueOrDefault();
            if (round <= 0)
            {
                errorMessage = "Race context is invalid. Round must be greater than zero.";
                return false;
            }

            lookup = new RaceRouteLookup
            {
                CompetitionSlug = competitionSlug,
                Season = seasonFromRoute.Value,
                LookupType = RaceRouteLookupType.Round,
                LookupValue = round.ToString()
            };
            return true;
        }

        if (hasSlug)
        {
            if (!TryNormalizeSlug(raceSlugFromRoute, out var raceSlug))
            {
                errorMessage = "Race context is invalid. Race slug must use letters, numbers, and hyphens.";
                return false;
            }

            lookup = new RaceRouteLookup
            {
                CompetitionSlug = competitionSlug,
                Season = seasonFromRoute.Value,
                LookupType = RaceRouteLookupType.Slug,
                LookupValue = raceSlug
            };
            return true;
        }

        errorMessage = "Race context is invalid. Missing race round or race slug.";
        return false;
    }

    private static bool TryNormalizeRaceId(string? raceId, out string normalizedRaceId)
    {
        normalizedRaceId = string.Empty;
        if (string.IsNullOrWhiteSpace(raceId))
        {
            return false;
        }

        var trimmed = raceId.Trim();
        foreach (var ch in trimmed)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_'))
            {
                return false;
            }
        }

        normalizedRaceId = trimmed;
        return true;
    }

    private static bool TryNormalizeSlug(string? value, out string normalized)
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

    private static bool IsCompatibilityRoute(string relativePath)
    {
        var pathOnly = relativePath.Split('?', '#')[0].Trim('/');
        return string.Equals(pathOnly, SelectionDefaults.CompatibilityRoutePath, StringComparison.OrdinalIgnoreCase);
    }
}