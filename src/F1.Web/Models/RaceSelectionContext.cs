namespace F1.Web.Models;

public sealed class RaceSelectionContext
{
    public string? RaceId { get; init; }
    public RaceRouteLookup? Lookup { get; init; }
    public required string ContextKey { get; init; }
}

public sealed class RaceRouteLookup
{
    public required string CompetitionSlug { get; init; }
    public required int Season { get; init; }
    public required RaceRouteLookupType LookupType { get; init; }
    public required string LookupValue { get; init; }
}

public enum RaceRouteLookupType
{
    Round,
    Slug
}
