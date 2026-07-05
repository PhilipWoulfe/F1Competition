using F1.Web.Configuration;
using F1.Web.Models;

namespace F1.Web.Tests.Configuration;

public class RaceSelectionRouteResolverTests
{
    [Fact]
    public void TryResolve_WhenRouteRaceIdProvided_ReturnsRouteContext()
    {
        var success = RaceSelectionRouteResolver.TryResolve("2026-01-australia", null, null, null, null, "selection/2026-01-australia", out var context, out var errorMessage);

        Assert.True(success);
        Assert.NotNull(context);
        Assert.Equal("2026-01-australia", context.RaceId);
        Assert.Null(context.Lookup);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryResolve_WhenCompetitionSeasonRoundProvided_ReturnsLookupContext()
    {
        var success = RaceSelectionRouteResolver.TryResolve(null, "main", 2026, 2, null, "selection/main/2026/round/2", out var context, out var errorMessage);

        Assert.True(success);
        Assert.NotNull(context);
        Assert.Null(context.RaceId);
        Assert.NotNull(context.Lookup);
        Assert.Equal("main", context.Lookup.CompetitionSlug);
        Assert.Equal(2026, context.Lookup.Season);
        Assert.Equal(RaceRouteLookupType.Round, context.Lookup.LookupType);
        Assert.Equal("2", context.Lookup.LookupValue);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryResolve_WhenCompetitionSeasonSlugProvided_ReturnsLookupContext()
    {
        var success = RaceSelectionRouteResolver.TryResolve(null, "main", 2026, null, "australian-grand-prix", "selection/main/2026/australian-grand-prix", out var context, out var errorMessage);

        Assert.True(success);
        Assert.NotNull(context);
        Assert.Null(context.RaceId);
        Assert.NotNull(context.Lookup);
        Assert.Equal("main", context.Lookup.CompetitionSlug);
        Assert.Equal(2026, context.Lookup.Season);
        Assert.Equal(RaceRouteLookupType.Slug, context.Lookup.LookupType);
        Assert.Equal("australian-grand-prix", context.Lookup.LookupValue);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryResolve_WhenLegacyPathUsed_ReturnsMissingContextError()
    {
        var success = RaceSelectionRouteResolver.TryResolve(null, null, null, null, null, "yas-marina-selection", out var context, out var errorMessage);

        Assert.False(success);
        Assert.Null(context);
        Assert.Equal("Race context is missing. Open this page using /selection/{raceId} or /selection/{competition}/{season}/round/{round}.", errorMessage);
    }

    [Fact]
    public void TryResolve_WhenRaceIdMissingAndNotCompatibilityPath_ReturnsMissingContextError()
    {
        var success = RaceSelectionRouteResolver.TryResolve(null, null, null, null, null, "selection", out var context, out var errorMessage);

        Assert.False(success);
        Assert.Null(context);
        Assert.Equal("Race context is missing. Open this page using /selection/{raceId} or /selection/{competition}/{season}/round/{round}.", errorMessage);
    }

    [Fact]
    public void TryResolve_WhenRaceIdContainsInvalidCharacters_ReturnsInvalidContextError()
    {
        var success = RaceSelectionRouteResolver.TryResolve("bad race id", null, null, null, null, "selection/bad%20race%20id", out var context, out var errorMessage);

        Assert.False(success);
        Assert.Null(context);
        Assert.Equal("Race context is invalid. Only letters, numbers, underscores, and hyphens are allowed.", errorMessage);
    }

    [Fact]
    public void TryResolve_WhenCompetitionSlugContainsInvalidCharacters_ReturnsInvalidContextError()
    {
        var success = RaceSelectionRouteResolver.TryResolve(null, "Bad Competition", 2026, 1, null, "selection/Bad Competition/2026/round/1", out var context, out var errorMessage);

        Assert.False(success);
        Assert.Null(context);
        Assert.Equal("Race context is invalid. Competition must be a slug with letters, numbers, and hyphens.", errorMessage);
    }
}
