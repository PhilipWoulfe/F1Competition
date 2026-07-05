using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;

namespace F1.E2E.Tests.Infrastructure;

internal class ApiVerificationClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string? _raceId;

    public ApiVerificationClient(E2eOptions options)
    {
        _raceId = options.RaceId;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(options.ApiBaseUrl + "/")
        };

        var headers = options.BuildCloudflareHeaders();
        foreach (var header in headers)
        {
            _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
        }
    }

    public async Task<IReadOnlyList<CurrentSelectionRow>> GetCurrentSelectionsAsync(string? raceId, CancellationToken cancellationToken)
    {
        var targetRaceId = string.IsNullOrWhiteSpace(raceId) ? _raceId : raceId;
        if (string.IsNullOrWhiteSpace(targetRaceId))
        {
            throw new InvalidOperationException("Race id is required for current selections lookup.");
        }

        var response = await _httpClient.GetAsync($"selections/{targetRaceId}/current", cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<List<CurrentSelectionRow>>(cancellationToken: cancellationToken);
        return payload ?? [];
    }

    public async Task<RaceMetadataRow?> GetRaceMetadataAsync(string raceId, bool includeDraft, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"races/{raceId}/metadata?includeDraft={includeDraft.ToString().ToLowerInvariant()}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RaceMetadataRow>(cancellationToken: cancellationToken);
    }

    public async Task<RaceConfigRow> GetRaceConfigAsync(string raceId, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"selections/{raceId}/config", cancellationToken);
        response.EnsureSuccessStatusCode();

        var config = await response.Content.ReadFromJsonAsync<RaceConfigRow>(cancellationToken: cancellationToken);
        if (config is null || string.IsNullOrWhiteSpace(config.RaceId))
        {
            throw new InvalidOperationException($"Race config payload was empty for race '{raceId}'.");
        }

        return config;
    }

    public async Task WaitForSelectionPersistenceAsync(string raceId, IReadOnlyList<string> expectedDriverIdsInOrder, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (expectedDriverIdsInOrder.Count == 0)
        {
            throw new ArgumentException("At least one expected driver id is required.", nameof(expectedDriverIdsInOrder));
        }

        if (expectedDriverIdsInOrder.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Expected driver ids cannot contain null, empty, or whitespace values.", nameof(expectedDriverIdsInOrder));
        }

        var deadline = DateTime.UtcNow + timeout;
        IReadOnlyList<CurrentSelectionRow> lastObservedRows = [];
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var rows = await GetCurrentSelectionsAsync(raceId, cancellationToken);
                lastObservedRows = rows;

                var orderedRows = rows.OrderBy(row => row.Position).ToList();
                if (orderedRows.Count == expectedDriverIdsInOrder.Count)
                {
                    var matches = orderedRows
                        .Select(row => row.DriverId)
                        .SequenceEqual(expectedDriverIdsInOrder, StringComparer.OrdinalIgnoreCase);
                    if (matches)
                    {
                        return;
                    }
                }
            }
            catch (HttpRequestException ex) when (IsTransientTransportFailure(ex))
            {
                // Transient proxy/API failures happen in CI; keep polling until timeout.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        var expectedSummary = string.Join(",", expectedDriverIdsInOrder.Select((driverId, index) => $"{index + 1}:{driverId}"));
        var observedSummary = lastObservedRows.Count == 0
            ? "<none>"
            : string.Join(",", lastObservedRows
                .OrderBy(row => row.Position)
                .Select(row => $"{row.Position}:{row.DriverId}"));

        throw new TimeoutException(
            $"Selection for race '{raceId}' was not persisted with the expected ordered set within {timeout.TotalSeconds} seconds. " +
            $"Expected ({expectedDriverIdsInOrder.Count}): [{expectedSummary}]. Observed ({lastObservedRows.Count}): [{observedSummary}].");
    }

    public async Task<RaceMetadataRow> WaitForMetadataAsync(string raceId, string expectedH2hQuestion, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var metadata = await GetRaceMetadataAsync(raceId, includeDraft: true, cancellationToken);
                if (metadata is not null && string.Equals(metadata.H2HQuestion, expectedH2hQuestion, StringComparison.Ordinal))
                {
                    return metadata;
                }
            }
            catch (HttpRequestException ex) when (IsTransientTransportFailure(ex))
            {
                // Transient proxy/API failures happen in CI; keep polling until timeout.
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        throw new TimeoutException($"Metadata update for race '{raceId}' was not observed within {timeout.TotalSeconds} seconds.");
    }

    public void SetMockDateHeader(string isoDate)
    {
        _httpClient.DefaultRequestHeaders.Remove("X-Mock-Date");
        _httpClient.DefaultRequestHeaders.Add("X-Mock-Date", isoDate);
    }

    public async Task<HttpResponseMessage> PutSelectionAsync(string raceId, object submission)
    {
        var response = await _httpClient.PutAsJsonAsync($"selections/{raceId}/mine", submission);
        return response;
    }

    public async Task<string> ResolveRaceIdByRoundAsync(string competitionSlug, int season, int round, CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync($"races/context/{competitionSlug}/{season}/round/{round}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"No race found for context {competitionSlug}/{season}/round/{round}.");
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<RaceContextResolutionRow>(cancellationToken: cancellationToken);
        if (payload is null || string.IsNullOrWhiteSpace(payload.RaceId))
        {
            throw new InvalidOperationException($"Race context resolution returned an empty payload for {competitionSlug}/{season}/round/{round}.");
        }

        return payload.RaceId;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task SetMockDate(string mockDateUtcIso, CancellationToken cancellationToken)
    {
        var parsed = DateTime.Parse(
            mockDateUtcIso,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        using var response = await _httpClient.PostAsJsonAsync("admin/mock-date", new { mockDateUtc = parsed }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ClearMockDate(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("admin/mock-date", new { mockDateUtc = (DateTime?)null }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static bool IsTransientStatus(HttpStatusCode? statusCode)
    {
        return statusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransientTransportFailure(HttpRequestException ex)
    {
        if (IsTransientStatus(ex.StatusCode))
        {
            return true;
        }

        return ex.InnerException is IOException or SocketException;
    }
}

internal class CurrentSelectionRow
{
    public int Position { get; set; }
    public string DriverId { get; set; } = string.Empty;
    public string SelectionType { get; set; } = string.Empty;
}

internal class RaceMetadataRow
{
    public string H2HQuestion { get; set; } = string.Empty;
    public string BonusQuestion { get; set; } = string.Empty;
    public bool IsPublished { get; set; }
}

internal sealed class RaceContextResolutionRow
{
    public string RaceId { get; set; } = string.Empty;
}

internal sealed class RaceConfigRow
{
    public string RaceId { get; set; } = string.Empty;
    public DateTime FinalDeadlineUtc { get; set; }
}
