using SharedCanonicalRaceId = F1.Core.Identifiers.CanonicalRaceId;

namespace F1.Api.Infrastructure;

public static class CanonicalRaceId
{
    private const string ValidationMessage = "Race ID must use canonical format: <competition>-<season>-<round>-<race-slug>.";

    public static bool IsValid(string raceId)
    {
        return SharedCanonicalRaceId.IsValid(raceId);
    }

    public static object BuildValidationErrorPayload()
    {
        return new { message = ValidationMessage };
    }
}
