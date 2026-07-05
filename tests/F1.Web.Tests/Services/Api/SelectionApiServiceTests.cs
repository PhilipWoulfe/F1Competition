using F1.Web.Models;
using F1.Web.Services.Api;
using System.Net;
using System.Text;
using System.Text.Json;

namespace F1.Web.Tests.Services.Api;

public class SelectionApiServiceTests
{
    [Fact]
    public async Task GetMineAsync_WhenResponseIsNotFound_ReturnsNull()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));
        var service = CreateService(handler);

        var result = await service.GetMineAsync("2025-24-yas_marina");

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveMineAsync_WhenSuccess_ReturnsDeserializedSelection()
    {
        var expected = new Selection
        {
            RaceId = "2025-24-yas_marina",
            UserId = "user@example.com",
            BetType = BetType.Regular,
            IsLocked = false
        };

        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(CreateJsonResponse(expected));
        var service = CreateService(handler);

        var submission = new SelectionSubmission
        {
            BetType = BetType.Regular,
            OrderedSelections = [new SelectionPosition { Position = 1, DriverId = "norris" }],
            Selections = ["norris"]
        };

        var result = await service.SaveMineAsync("2025-24-yas_marina", submission);

        Assert.Equal("2025-24-yas_marina", result.RaceId);
        Assert.Equal("user@example.com", result.UserId);
        Assert.Equal(BetType.Regular, result.BetType);
    }

    [Fact]
    public async Task SaveMineAsync_WhenSuccessBodyIsEmpty_ThrowsApiServiceException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        var submission = new SelectionSubmission
        {
            BetType = BetType.Regular,
            OrderedSelections = [new SelectionPosition { Position = 1, DriverId = "norris" }],
            Selections = ["norris"]
        };

        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.SaveMineAsync("2025-24-yas_marina", submission));

        Assert.Equal(HttpStatusCode.OK, ex.Error.StatusCode);
        Assert.Contains("empty response body", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveMineAsync_WhenSuccessBodyIsMalformedJson_ThrowsApiServiceException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ broken json", Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        var submission = new SelectionSubmission
        {
            BetType = BetType.Regular,
            OrderedSelections = [new SelectionPosition { Position = 1, DriverId = "norris" }],
            Selections = ["norris"]
        };

        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.SaveMineAsync("2025-24-yas_marina", submission));

        Assert.Equal(HttpStatusCode.OK, ex.Error.StatusCode);
        Assert.Contains("malformed JSON", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveMineAsync_WhenApiReturnsJsonError_ThrowsApiServiceException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(CreateJsonResponse(new { message = "Validation failed", code = "selection_invalid" }, HttpStatusCode.BadRequest));
        var service = CreateService(handler);

        var submission = new SelectionSubmission
        {
            BetType = BetType.PreQualy,
            OrderedSelections = [new SelectionPosition { Position = 1, DriverId = "norris" }],
            Selections = ["norris"]
        };

        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.SaveMineAsync("2025-24-yas_marina", submission));

        Assert.Equal(HttpStatusCode.BadRequest, ex.Error.StatusCode);
        Assert.Equal("Validation failed", ex.Error.Message);
        Assert.Equal("selection_invalid", ex.Error.Code);
    }

    [Fact]
    public async Task GetCurrentAsync_WhenPayloadIsMalformed_ThrowsApiServiceException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{ malformed", Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.GetCurrentAsync("2025-24-yas_marina"));

        Assert.Equal(HttpStatusCode.OK, ex.Error.StatusCode);
        Assert.Contains("malformed JSON", ex.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetConfigAsync_WhenServerReturnsPlainTextError_ThrowsApiServiceException()
    {
        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Backend unavailable", Encoding.UTF8, "text/plain")
        });
        var service = CreateService(handler);

        var ex = await Assert.ThrowsAsync<ApiServiceException>(() => service.GetConfigAsync("2025-24-yas_marina"));

        Assert.Equal(HttpStatusCode.InternalServerError, ex.Error.StatusCode);
        Assert.Equal("Backend unavailable", ex.Error.Message);
    }

    private static SelectionApiService CreateService(QueueHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new SelectionApiService(httpClient);
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
