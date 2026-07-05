using Bunit.TestDoubles;
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
    }

    [Fact]
    public void NavMenu_ShouldShowAdminLink_WhenInAdminRole()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetRoles("Admin");

        var cut = Render<NavMenu>();

        Assert.Contains("Admin", cut.Markup);
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
