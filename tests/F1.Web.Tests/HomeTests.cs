using F1.Web.Configuration;
using F1.Web.Models;
using F1.Web.Pages;
using F1.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace F1.Web.Tests.Pages;

public class HomeTests : BunitContext
{
    [Fact]
    public void Home_ShouldRedirectAdminUsers_ToConfiguredAdminLandingPath()
    {
        var userSession = CreateUserSession(new User
        {
            Email = "admin@example.com",
            IsAdmin = true,
            IsAuthenticated = true
        });

        ConfigureServices(userSession.Object, options =>
        {
            options.AdminLandingPath = "/admin/migration-runs";
            options.AuthenticatedUserLandingPath = "/results";
            options.FallbackPath = "/results";
        });

        Render<Home>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/admin/migration-runs", navigation.Uri);
    }

    [Fact]
    public void Home_ShouldRedirectAuthenticatedNonAdminUsers_ToConfiguredUserLandingPath()
    {
        var userSession = CreateUserSession(new User
        {
            Email = "user@example.com",
            IsAuthenticated = true
        });

        ConfigureServices(userSession.Object, options =>
        {
            options.AdminLandingPath = "/admin/migration-runs";
            options.AuthenticatedUserLandingPath = "/results";
            options.FallbackPath = "/drivers";
        });

        Render<Home>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/results", navigation.Uri);
    }

    [Fact]
    public void Home_ShouldFallback_WhenRoleSpecificLandingPathIsBlank()
    {
        var userSession = CreateUserSession(new User
        {
            Email = "user@example.com",
            IsAuthenticated = true
        });

        ConfigureServices(userSession.Object, options =>
        {
            options.AdminLandingPath = "/admin/migration-runs";
            options.AuthenticatedUserLandingPath = "   ";
            options.FallbackPath = "/drivers";
        });

        Render<Home>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.Equal("http://localhost/drivers", navigation.Uri);
    }

    private void ConfigureServices(IUserSession userSession, Action<PostLoginRoutingOptions> configureOptions)
    {
        Services.AddSingleton(userSession);
        Services.AddScoped<IPostLoginLandingResolver, PostLoginLandingResolver>();
        Services.Configure(configureOptions);
    }

    private static Mock<IUserSession> CreateUserSession(User? user)
    {
        var userSession = new Mock<IUserSession>();
        userSession.SetupGet(session => session.User).Returns(user);
        userSession.Setup(session => session.InitializeAsync()).Returns(Task.CompletedTask);
        return userSession;
    }
}