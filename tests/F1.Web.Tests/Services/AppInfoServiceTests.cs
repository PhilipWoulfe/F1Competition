using System.Net;
using System.Text;
using F1.Web.Services;
using Moq;
using Moq.Protected;

namespace F1.Web.Tests.Services;

public class AppInfoServiceTests
{
    [Fact]
    public async Task GetShortVersionAsync_WhenVersionExists_ReturnsTrimmedHash()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"version\":\"abcdef123456\"}", Encoding.UTF8, "application/json")
        });

        var result = await service.GetShortVersionAsync();

        Assert.Equal("abcdef1", result);
    }

    [Fact]
    public async Task GetShortVersionAsync_WhenVersionMissing_ReturnsNa()
    {
        var service = CreateService(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"version\":null}", Encoding.UTF8, "application/json")
        });

        var result = await service.GetShortVersionAsync();

        Assert.Equal("N/A", result);
    }

    [Fact]
    public async Task GetShortVersionAsync_WhenHttpThrows_ReturnsError()
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost") };
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(AppInfoService.HostClientName))
            .Returns(client);

        var service = new AppInfoService(factory.Object);

        var result = await service.GetShortVersionAsync();

        Assert.Equal("Error", result);
    }

    private static AppInfoService CreateService(HttpResponseMessage response)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost") };
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(AppInfoService.HostClientName))
            .Returns(client);

        return new AppInfoService(factory.Object);
    }
}