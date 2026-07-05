using F1.Web.Configuration;

namespace F1.Web.Tests.Configuration;

public class RaceSelectionRouteResolverTests
{
    [Fact]
    public void TryResolve_WhenRouteRaceIdProvided_ReturnsRouteContext()
    {
        var success = RaceSelectionRouteResolver.TryResolve("2026-01-australia", "selection/2026-01-australia", out var context, out var errorMessage);

        Assert.True(success);
        Assert.NotNull(context);
        Assert.Equal("2026-01-australia", context.RaceId);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryResolve_WhenCompatibilityPathUsed_ReturnsCompatibilityRaceContext()
    {
        var success = RaceSelectionRouteResolver.TryResolve(null, SelectionDefaults.CompatibilityRoutePath, out var context, out var errorMessage);

        Assert.True(success);
        Assert.NotNull(context);
        Assert.Equal(SelectionDefaults.CompatibilityRaceId, context.RaceId);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryResolve_WhenRaceIdMissingAndNotCompatibilityPath_ReturnsMissingContextError()
    {
        var success = RaceSelectionRouteResolver.TryResolve(null, "selection", out var context, out var errorMessage);

        Assert.False(success);
        Assert.Null(context);
        Assert.Equal("Race context is missing. Open this page using /selection/{raceId}.", errorMessage);
    }

    [Fact]
    public void TryResolve_WhenRaceIdContainsInvalidCharacters_ReturnsInvalidContextError()
    {
        var success = RaceSelectionRouteResolver.TryResolve("bad race id", "selection/bad%20race%20id", out var context, out var errorMessage);

        Assert.False(success);
        Assert.Null(context);
        Assert.Equal("Race context is invalid. Only letters, numbers, underscores, and hyphens are allowed.", errorMessage);
    }
}
