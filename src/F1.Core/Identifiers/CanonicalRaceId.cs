using System.Text.RegularExpressions;

namespace F1.Core.Identifiers;

public static partial class CanonicalRaceId
{
    [GeneratedRegex("^(?:[a-z0-9]+(?:-[a-z0-9]+)*)-(?:\\d{4})-(?:[1-9]\\d*)-(?:[a-z0-9]+(?:-[a-z0-9]+)*)$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalRaceIdRegex();

    public static bool IsValid(string? raceId)
    {
        return !string.IsNullOrWhiteSpace(raceId)
               && CanonicalRaceIdRegex().IsMatch(raceId);
    }

    public static bool TryNormalize(string? raceId, out string normalizedRaceId)
    {
        normalizedRaceId = string.Empty;
        if (string.IsNullOrWhiteSpace(raceId))
        {
            return false;
        }

        var trimmed = raceId.Trim().ToLowerInvariant();
        if (!CanonicalRaceIdRegex().IsMatch(trimmed))
        {
            return false;
        }

        normalizedRaceId = trimmed;
        return true;
    }
}
