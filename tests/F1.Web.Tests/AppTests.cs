using Bunit.TestDoubles;
using F1.Web;
using F1.Web.Models;
using F1.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;

namespace F1.Web.Tests;

public class AppTests : BunitContext
{
    [Fact]
    public void App_ShouldShowAccessDenied_WhenNonAdminNavigatesToAdminLandingRoute()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("user@example.com");

        ConfigureCommonServices();

        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/admin/migration-runs");

        var cut = Render<App>();

        cut.WaitForAssertion(() => Assert.Contains("Access Denied", cut.Markup));
    }

    private void ConfigureCommonServices()
    {
        var userSession = new Mock<IUserSession>();
        userSession.SetupGet(session => session.User).Returns(new User { Email = "user@example.com" });
        userSession.Setup(session => session.InitializeAsync()).Returns(Task.CompletedTask);

        var appInfoService = new Mock<IAppInfoService>();
        appInfoService.Setup(service => service.GetShortVersionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("abcdef1");

        Services.AddSingleton(userSession.Object);
        Services.AddSingleton(appInfoService.Object);
        Services.AddSingleton<IWebAssemblyHostEnvironment>(new TestHostEnvironment("Test"));
        Services.AddSingleton(CreateMockHttpClient());
        Services.AddSingleton<IMockDateService, MockDateService>();
    }

    private static HttpClient CreateMockHttpClient()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(request => request.RequestUri!.ToString().Contains("admin/mock-date")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{\"mockDate\":null}", System.Text.Encoding.UTF8, "application/json")
            });

        return new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class TestHostEnvironment(string environment) : IWebAssemblyHostEnvironment
    {
        public string Environment { get; } = environment;
        public string ApplicationName => "F1.Web.Tests";
        public string BaseAddress => "http://localhost/";
    }
}