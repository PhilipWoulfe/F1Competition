namespace F1.Web.Models;

public sealed class RaceContextResolution
{
    public required string RaceId { get; init; }
    public required string CompetitionSlug { get; init; }
    public required int Season { get; init; }
    public required int Round { get; init; }
    public required string RaceSlug { get; init; }
}
