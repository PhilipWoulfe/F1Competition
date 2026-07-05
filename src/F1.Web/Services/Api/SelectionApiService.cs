using F1.Web.Models;
using System.Net;
using System.Net.Http.Json;

namespace F1.Web.Services.Api;

public sealed class SelectionApiService(HttpClient httpClient) : ISelectionApiService
{
    public async Task<RaceConfig> GetConfigAsync(string raceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raceId);

        using var response = await httpClient.GetAsync($"selections/{raceId}/config", cancellationToken);
        return await ApiResponseParser.ReadRequiredJsonAsync<RaceConfig>(response, "Loading race selection config", cancellationToken);
    }

    public async Task<Selection?> GetMineAsync(string raceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raceId);

        using var response = await httpClient.GetAsync($"selections/{raceId}/mine", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponseParser.ReadOptionalJsonAsync<Selection?>(response, null, "Loading my race selection", cancellationToken);
    }

    public async Task<CurrentSelectionItem[]> GetCurrentAsync(string raceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raceId);

        using var response = await httpClient.GetAsync($"selections/{raceId}/current", cancellationToken);
        return await ApiResponseParser.ReadOptionalJsonAsync(response, Array.Empty<CurrentSelectionItem>(), "Loading current selections", cancellationToken);
    }

    public async Task<Selection> SaveMineAsync(string raceId, SelectionSubmission submission, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(raceId);
        ArgumentNullException.ThrowIfNull(submission);

        using var response = await httpClient.PutAsJsonAsync($"selections/{raceId}/mine", submission, cancellationToken);
        return await ApiResponseParser.ReadRequiredJsonAsync<Selection>(response, "Saving race selection", cancellationToken);
    }
}

