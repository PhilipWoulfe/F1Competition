using Bunit.TestDoubles;
using F1.Web.Configuration;
using F1.Web.Layout;

namespace F1.Web.Tests.Layout;

public class NavMenuTests : BunitContext
{
    [Fact]
    public void NavMenu_ShouldHideAuthorizedLinks_WhenAnonymous()
    {
        var auth = this.AddAuthorization();
        auth.SetNotAuthorized();

        var cut = Render<NavMenu>();

        Assert.DoesNotContain("Driver Standings", cut.Markup);
        Assert.DoesNotContain("Race Selection", cut.Markup);
        Assert.DoesNotContain("Drivers", cut.Markup);
    }

    [Fact]
    public void NavMenu_ShouldShowAuthorizedLinks_WhenAuthenticated()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("user@example.com");

        var cut = Render<NavMenu>();

        Assert.Contains("Driver Standings", cut.Markup);
        Assert.Contains("Race Selection", cut.Markup);
        Assert.Contains("Drivers", cut.Markup);

        var selectionHref = $"selection/{SelectionDefaults.DefaultCompetitionSlug}/{SelectionDefaults.DefaultSeason}/round/{SelectionDefaults.DefaultRound}";
        Assert.Contains($"href=\"{selectionHref}\"", cut.Markup);
    }

    [Fact]
    public void NavMenu_ShouldShowAdminLink_WhenInAdminRole()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetRoles("Admin");

        var cut = Render<NavMenu>();

        Assert.Contains("Admin", cut.Markup);
        Assert.Contains("Migration Runs", cut.Markup);
        Assert.Contains("href=\"admin/migration-runs\"", cut.Markup);
    }

    [Fact]
    public void NavMenu_AdminLink_ShouldFollowAuthStateTransitions()
    {
        var auth = this.AddAuthorization();
        auth.SetNotAuthorized();

        var cut = Render<NavMenu>();
        Assert.DoesNotContain("href=\"admin/migration-runs\"", cut.Markup);

        auth.SetAuthorized("user@example.com");
        cut.Render();
        Assert.DoesNotContain("href=\"admin/migration-runs\"", cut.Markup);

        auth.SetRoles("Admin");
        cut.Render();
        Assert.Contains("href=\"admin/migration-runs\"", cut.Markup);

        auth.SetNotAuthorized();
        cut.Render();
        Assert.DoesNotContain("href=\"admin/migration-runs\"", cut.Markup);
    }

    [Fact]
    public void NavMenu_ShouldToggleCollapsedState_WhenClicked()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("user@example.com");

        var cut = Render<NavMenu>();

        Assert.Contains("collapse", cut.Markup);
        cut.Find("button.navbar-toggler").Click();
        Assert.DoesNotContain("collapse nav-scrollable", cut.Markup);
    }
}
