using F1.Web.Models;
using F1.Web.Services.Api;
using System.Net;
using System.Text;
using System.Text.Json;

namespace F1.Web.Tests.Services.Api;

public class RaceContextApiServiceTests
{
    [Fact]
    public async Task ResolveByRoundAsync_WhenFound_ReturnsResolution()
    {
        var expected = new RaceContextResolution
        {
            RaceId = "main-2026-2-australian-grand-prix",
            CompetitionSlug = "main",
            Season = 2026,
            Round = 2,
            RaceSlug = "australian-grand-prix"
        };

        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(CreateJsonResponse(expected));
        var service = CreateService(handler);

        var result = await service.ResolveByRoundAsync("main", 2026, 2);

        Assert.NotNull(result);
        Assert.Equal(expected.RaceId, result.RaceId);
    }

    [Fact]
    public async Task ResolveBySlugAsync_WhenNotFound_ReturnsNull()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var result = await service.ResolveBySlugAsync("main", 2026, "unknown");

        Assert.Null(result);
    }

    private static RaceContextApiService CreateService(QueueHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new RaceContextApiService(httpClient);
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
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
