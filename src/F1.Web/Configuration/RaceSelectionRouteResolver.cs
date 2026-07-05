using F1.Web.Models;

namespace F1.Web.Configuration;

public static class RaceSelectionRouteResolver
{
    public static bool TryResolve(string? raceIdFromRoute, string relativePath, out RaceSelectionContext? context, out string? errorMessage)
    {
        context = null;
        errorMessage = null;

        if (TryNormalizeRaceId(raceIdFromRoute, out var normalizedRaceId))
        {
            context = new RaceSelectionContext { RaceId = normalizedRaceId };
            return true;
        }

        if (IsCompatibilityRoute(relativePath))
        {
            context = new RaceSelectionContext { RaceId = SelectionDefaults.CompatibilityRaceId };
            return true;
        }

        errorMessage = string.IsNullOrWhiteSpace(raceIdFromRoute)
            ? "Race context is missing. Open this page using /selection/{raceId}."
            : "Race context is invalid. Only letters, numbers, underscores, and hyphens are allowed.";
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

    private static bool IsCompatibilityRoute(string relativePath)
    {
        var pathOnly = relativePath.Split('?', '#')[0].Trim('/');
        return string.Equals(pathOnly, SelectionDefaults.CompatibilityRoutePath, StringComparison.OrdinalIgnoreCase);
    }
}