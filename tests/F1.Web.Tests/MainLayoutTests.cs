using Bunit.TestDoubles;
using F1.Web.Layout;
using F1.Web.Models;
using F1.Web.Services;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Protected;

namespace F1.Web.Tests.Layout;

public class MainLayoutTests : BunitContext
{
    [Fact]
    public void MainLayout_ShouldShowDevelopmentBanner_AndToggleButton_InDevelopment()
    {
        var auth = this.AddAuthorization();
        auth.SetNotAuthorized();

        var userSession = new Mock<IUserSession>();
        userSession.Setup(x => x.InitializeAsync()).Returns(Task.CompletedTask);
        userSession.SetupGet(x => x.User).Returns(new User { Email = "dev@example.com" });

        ConfigureCommonServices("Development", userSession.Object);

        var cut = Render<MainLayout>(parameters =>
            parameters.Add(p => p.Body, (builder) => builder.AddMarkupContent(0, "<p>Body</p>")));

        cut.WaitForAssertion(() => Assert.Contains("DEVELOPMENT MODE", cut.Markup));
        Assert.Contains("Toggle Auth", cut.Markup);
    }

    [Fact]
    public void MainLayout_ShouldShowQaBanner_InTestEnvironment()
    {
        var auth = this.AddAuthorization();
        auth.SetNotAuthorized();

        var userSession = new Mock<IUserSession>();
        userSession.Setup(x => x.InitializeAsync()).Returns(Task.CompletedTask);

        ConfigureCommonServices("Test", userSession.Object);

        var cut = Render<MainLayout>(parameters =>
            parameters.Add(p => p.Body, (builder) => builder.AddMarkupContent(0, "<p>Body</p>")));

        cut.WaitForAssertion(() => Assert.Contains("QA ENVIRONMENT", cut.Markup));
    }

    [Fact]
    public void MainLayout_ShouldShowVersionFooter_InProduction()
    {
        var auth = this.AddAuthorization();
        auth.SetNotAuthorized();

        var userSession = new Mock<IUserSession>();
        userSession.Setup(x => x.InitializeAsync()).Returns(Task.CompletedTask);

        ConfigureCommonServices("Production", userSession.Object);

        var cut = Render<MainLayout>(parameters =>
            parameters.Add(p => p.Body, (builder) => builder.AddMarkupContent(0, "<p>Body</p>")));

        cut.WaitForAssertion(() => Assert.Contains("v. ", cut.Markup));
    }

    private void ConfigureCommonServices(string environment, IUserSession userSession)
    {
        var appInfoService = new Mock<IAppInfoService>();
        appInfoService.Setup(x => x.GetShortVersionAsync(It.IsAny<CancellationToken>())).ReturnsAsync("abcdef1");

        Services.AddSingleton(userSession);
        Services.AddSingleton(appInfoService.Object);
        Services.AddSingleton<IWebAssemblyHostEnvironment>(new TestHostEnvironment(environment));
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
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri!.ToString().Contains("admin/mock-date")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{\"mockDate\":null}", System.Text.Encoding.UTF8, "application/json")
            });
        return new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost") };
    }

    private sealed class TestHostEnvironment : IWebAssemblyHostEnvironment
    {
        public TestHostEnvironment(string environment)
        {
            Environment = environment;
        }

        public string Environment { get; }
        public string ApplicationName => "F1.Web.Tests";
        public string BaseAddress => "http://localhost/";
    }
}
