using F1.Web.Models;
using F1.Web.Services.Api;
using System.Net;
using System.Text;

namespace F1.Web.Tests.Services.Api;

public class DriversAndMetadataApiServiceTests
{
    private const string RaceId = "main-2025-24-abu-dhabi-grand-prix";

    [Fact]
    public async Task DriversApiService_GetAllAsync_WhenPayloadIsMalformed_ThrowsApiServiceException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ malformed", Encoding.UTF8, "application/json")
        });

        var service = new DriversApiService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.GetAllAsync());

        Assert.Equal(HttpStatusCode.OK, ex.Error.StatusCode);
        Assert.Contains("malformed JSON", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RaceMetadataApiService_GetPublishedAsync_WhenNotFound_ReturnsNull()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));

        var service = new RaceMetadataApiService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var result = await service.GetPublishedAsync(RaceId);

        Assert.Null(result);
    }

    [Fact]
    public async Task RaceMetadataApiService_GetPublishedAsync_WhenServerError_ThrowsApiServiceException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("Proxy upstream failure", Encoding.UTF8, "text/plain")
        });

        var service = new RaceMetadataApiService(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.GetPublishedAsync(RaceId));

        Assert.Equal(HttpStatusCode.BadGateway, ex.Error.StatusCode);
        Assert.Equal("Proxy upstream failure", ex.Error.Message);
    }

    private sealed class QueueHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public void EnqueueResponse(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response for request.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
