using System.Text.RegularExpressions;

namespace F1.Api.Infrastructure;

public static partial class CanonicalRaceId
{
    private const string ValidationMessage = "Race ID must use canonical format: <competition>-<season>-<round>-<race-slug>.";

    [GeneratedRegex("^(?:[a-z0-9]+(?:-[a-z0-9]+)*)-(?:\\d{4})-(?:[1-9]\\d*)-(?:[a-z0-9]+(?:-[a-z0-9]+)*)$", RegexOptions.CultureInvariant)]
    private static partial Regex CanonicalRaceIdRegex();

    public static bool IsValid(string raceId)
    {
        return !string.IsNullOrWhiteSpace(raceId)
               && CanonicalRaceIdRegex().IsMatch(raceId);
    }

    public static object BuildValidationErrorPayload()
    {
        return new { message = ValidationMessage };
    }
}
