using F1.Web.Models;
using System.Net;

namespace F1.Web.Services.Api;

public sealed class MigrationRunsApiService(HttpClient httpClient) : IMigrationRunsApiService
{
    public async Task<AdminMigrationRunListResponse> GetRunsAsync(
        int page,
        int pageSize,
        string? status = null,
        DateTime? startedFromUtc = null,
        DateTime? startedToUtc = null,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>
        {
            $"page={Math.Max(1, page)}",
            $"pageSize={Math.Max(1, pageSize)}"
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            queryParts.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (startedFromUtc.HasValue)
        {
            queryParts.Add($"startedFromUtc={Uri.EscapeDataString(startedFromUtc.Value.ToString("o"))}");
        }

        if (startedToUtc.HasValue)
        {
            queryParts.Add($"startedToUtc={Uri.EscapeDataString(startedToUtc.Value.ToString("o"))}");
        }

        var path = $"admin/migration-runs?{string.Join("&", queryParts)}";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        return await ApiResponseParser.ReadRequiredJsonAsync<AdminMigrationRunListResponse>(
            response,
            "Loading migration runs",
            cancellationToken);
    }

    public async Task<AdminMigrationRunDetailResponse?> GetRunDetailAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"admin/migration-runs/{runId}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponseParser.ReadOptionalJsonAsync<AdminMigrationRunDetailResponse?>(
            response,
            null,
            "Loading migration run details",
            cancellationToken);
    }

    public string GetRunDiffExportUrl(Guid runId, string exportType, string format)
    {
        var safeExportType = Uri.EscapeDataString(exportType.Trim().ToLowerInvariant());
        var safeFormat = Uri.EscapeDataString(format.Trim().ToLowerInvariant());
        return $"admin/migration-runs/{runId}/exports/{safeExportType}?format={safeFormat}";
    }
}
