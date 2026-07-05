namespace F1.Core.Dtos;

public sealed class RaceContextResolutionDto
{
    public required string RaceId { get; init; }
    public required string CompetitionSlug { get; init; }
    public required int Season { get; init; }
    public required int Round { get; init; }
    public required string RaceSlug { get; init; }
}
