using F1.Web.Models;
using F1.Web.Services.Api;
using System.Net;
using System.Text;
using System.Text.Json;

namespace F1.Web.Tests.Services.Api;

public sealed class MigrationRunsApiServiceTests
{
    [Fact]
    public async Task StartRunFromUploadAsync_WhenWriteModeAndConfirmed_SendsConfirmNonEmptyStrategyFormField()
    {
        var runId = Guid.NewGuid();
        var responsePayload = new AdminMigrationRunKickoffResponse(
            RunId: runId,
            Status: "Queued",
            IsDryRun: false,
            RequestedMode: "write",
            SourceFilePath: "data/imports/uploads/import.csv",
            SourceFileChecksum: "abc123",
            TriggeredAtUtc: DateTime.UtcNow,
            RequestedBy: "admin@example.com",
            NonEmptyDbStrategy: "merge_upsert_active_records",
            CanonicalDataPresent: true,
            ExistingDriverCount: 1,
            ExistingRaceCount: 1,
            ExistingSelectionCount: 1,
            EstimatedAffectedRaceCount: 1,
            EstimatedAffectedParticipantCount: 1,
            EstimatedAffectedSelectionCount: 1);

        var handler = new CaptureHttpMessageHandler();
        handler.EnqueueResponse(CreateJsonResponse(responsePayload));
        var service = CreateService(handler);

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("Question,Philip\nAUS-1,VER"));
        var result = await service.StartRunFromUploadAsync(new AdminMigrationRunKickoffUploadRequest(
            FileName: "import.csv",
            Content: content,
            SourceProfile: "phil-2025-csv",
            Mode: "write",
            ConfirmNonEmptyStrategy: true));

        Assert.Equal(runId, result.RunId);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("http://localhost/admin/migration-runs/kickoff/upload", handler.LastRequest.RequestUri!.ToString());

        var multipart = await handler.LastRequest.Content!.ReadAsStringAsync();
        Assert.Contains("SourceProfile", multipart, StringComparison.Ordinal);
        Assert.Contains("\r\n\r\nphil-2025-csv\r\n", multipart, StringComparison.Ordinal);
        Assert.Contains("ConfirmNonEmptyStrategy", multipart, StringComparison.Ordinal);
        Assert.Contains("\r\n\r\ntrue\r\n", multipart, StringComparison.Ordinal);
    }

    private static MigrationRunsApiService CreateService(CaptureHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };

        return new MigrationRunsApiService(httpClient);
    }

    private static HttpResponseMessage CreateJsonResponse<T>(T payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
    }

    private sealed class CaptureHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public HttpRequestMessage? LastRequest { get; private set; }

        public void EnqueueResponse(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = request.Content is null
                    ? null
                    : new StringContent(await request.Content.ReadAsStringAsync(cancellationToken), Encoding.UTF8, request.Content.Headers.ContentType?.MediaType ?? "text/plain")
            };

            foreach (var header in request.Headers)
            {
                LastRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP response for request.");
            }

            return _responses.Dequeue();
        }
    }
}