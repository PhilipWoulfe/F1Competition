using Bunit.TestDoubles;
using F1.Web.Models;
using F1.Web.Pages;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace F1.Web.Tests.Pages;

public class AdminTests : BunitContext
{
    private const string RaceId = "main-2025-24-abu-dhabi-grand-prix";

    [Fact]
    public void Admin_ShouldRenderMetadata_WhenDraftExists()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetRoles("Admin");

        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(CreateJsonResponse(new RaceQuestionMetadata
        {
            RaceId = RaceId,
            H2HQuestion = "Who finishes higher: Leclerc or Norris?",
            BonusQuestion = "How many safety-car laps?",
            IsPublished = false,
            ETag = "etag-1",
            UpdatedAtUtc = DateTime.UtcNow
        }));

        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var cut = Render<Admin>();

        cut.WaitForAssertion(() => Assert.Contains("Race Question Admin", cut.Markup));
        Assert.Contains("Who finishes higher: Leclerc or Norris?", cut.Markup);
        Assert.Contains("How many safety-car laps?", cut.Markup);
    }

    [Fact]
    public void Admin_ShouldSaveMetadata_WhenSubmitSucceeds()
    {
        var auth = this.AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetRoles("Admin");

        var handler = new QueueHttpMessageHandler();
        handler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.EnqueueResponse(CreateJsonResponse(new RaceQuestionMetadata
        {
            RaceId = RaceId,
            H2HQuestion = "Who finishes higher: Leclerc or Norris?",
            BonusQuestion = "How many safety-car laps?",
            IsPublished = true,
            ETag = "etag-2",
            UpdatedAtUtc = DateTime.UtcNow
        }));

        Services.AddSingleton(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var cut = Render<Admin>();
        cut.WaitForAssertion(() => Assert.Contains("Race Question Admin", cut.Markup));

        cut.Find("#h2h-question").Change("Who finishes higher: Russell or Hamilton?");
        cut.Find("#bonus-question").Change("How many DNFs?");
        cut.Find("#publish-toggle").Change(true);
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => Assert.Contains("Metadata saved and published.", cut.Markup));
        Assert.Contains(handler.Requests, req => req.Method == HttpMethod.Put && req.RequestUri?.AbsolutePath.EndsWith($"/races/{RaceId}/metadata") == true);
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
        public List<HttpRequestMessage> Requests { get; } = [];

        public void EnqueueResponse(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response for request.");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
