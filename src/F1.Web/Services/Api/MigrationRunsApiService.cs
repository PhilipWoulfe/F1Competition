using F1.Web.Models;
using System.Net;
using System.Net.Http.Json;

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

    public async Task<AdminMigrationRunDetailResponse?> GetRunDetailAsync(
        Guid runId,
        CancellationToken cancellationToken = default,
        string? expectedStatus = null)
    {
        var path = $"admin/migration-runs/{runId}";
        if (!string.IsNullOrWhiteSpace(expectedStatus) && !string.Equals(expectedStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            path += $"?expectedStatus={Uri.EscapeDataString(expectedStatus)}";
        }

        using var response = await httpClient.GetAsync(path, cancellationToken);
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

    public async Task<AdminMigrationQuestionDiffListResponse?> GetQuestionDiffsAsync(
        Guid runId,
        int page,
        int pageSize,
        string? category = null,
        string? participant = null,
        string? expectedStatus = null,
        bool nonZeroDeltaOnly = false,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>
        {
            $"page={Math.Max(1, page)}",
            $"pageSize={Math.Max(1, pageSize)}"
        };

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            queryParts.Add($"category={Uri.EscapeDataString(category)}");
        }

        if (!string.IsNullOrWhiteSpace(participant))
        {
            queryParts.Add($"participant={Uri.EscapeDataString(participant)}");
        }

        if (!string.IsNullOrWhiteSpace(expectedStatus) && !string.Equals(expectedStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            queryParts.Add($"expectedStatus={Uri.EscapeDataString(expectedStatus)}");
        }

        if (nonZeroDeltaOnly)
        {
            queryParts.Add("nonZeroDeltaOnly=true");
        }

        var path = $"admin/migration-runs/{runId}/question-diffs?{string.Join("&", queryParts)}";
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponseParser.ReadOptionalJsonAsync<AdminMigrationQuestionDiffListResponse?>(
            response,
            null,
            "Loading question diffs",
            cancellationToken);
    }

    public async Task<AdminMigrationQuestionDiffSummaryResponse?> GetQuestionSummaryAsync(
        Guid runId,
        string? category = null,
        string? participant = null,
        string? expectedStatus = null,
        bool nonZeroDeltaOnly = false,
        CancellationToken cancellationToken = default)
    {
        var queryParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            queryParts.Add($"category={Uri.EscapeDataString(category)}");
        }

        if (!string.IsNullOrWhiteSpace(participant))
        {
            queryParts.Add($"participant={Uri.EscapeDataString(participant)}");
        }

        if (!string.IsNullOrWhiteSpace(expectedStatus) && !string.Equals(expectedStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            queryParts.Add($"expectedStatus={Uri.EscapeDataString(expectedStatus)}");
        }

        if (nonZeroDeltaOnly)
        {
            queryParts.Add("nonZeroDeltaOnly=true");
        }

        var path = $"admin/migration-runs/{runId}/question-summary";
        if (queryParts.Count > 0)
        {
            path += $"?{string.Join("&", queryParts)}";
        }

        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ApiResponseParser.ReadOptionalJsonAsync<AdminMigrationQuestionDiffSummaryResponse?>(
            response,
            null,
            "Loading question summary",
            cancellationToken);
    }

    public async Task<AdminMigrationRunKickoffResponse> StartRunAsync(AdminMigrationRunKickoffRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("admin/migration-runs/kickoff", request, cancellationToken);
        return await ApiResponseParser.ReadRequiredJsonAsync<AdminMigrationRunKickoffResponse>(
            response,
            "Starting migration run",
            cancellationToken);
    }

    public async Task<AdminMigrationRunKickoffResponse> StartRunFromUploadAsync(AdminMigrationRunKickoffUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(request.Content);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/csv");
        content.Add(fileContent, "SourceFile", request.FileName);
        content.Add(new StringContent(request.Mode), "Mode");

        using var response = await httpClient.PostAsync("admin/migration-runs/kickoff/upload", content, cancellationToken);
        return await ApiResponseParser.ReadRequiredJsonAsync<AdminMigrationRunKickoffResponse>(
            response,
            "Starting migration run from upload",
            cancellationToken);
    }

    public string GetRunDiffExportUrl(
        Guid runId,
        string exportType,
        string format,
        string? expectedStatus = null,
        string? category = null,
        string? participant = null,
        bool nonZeroDeltaOnly = false)
    {
        var safeExportType = Uri.EscapeDataString(exportType.Trim().ToLowerInvariant());
        var safeFormat = Uri.EscapeDataString(format.Trim().ToLowerInvariant());
        var relativePath = $"admin/migration-runs/{runId}/exports/{safeExportType}?format={safeFormat}";
        if (!string.IsNullOrWhiteSpace(expectedStatus) && !string.Equals(expectedStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            relativePath += $"&expectedStatus={Uri.EscapeDataString(expectedStatus)}";
        }

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            relativePath += $"&category={Uri.EscapeDataString(category)}";
        }

        if (!string.IsNullOrWhiteSpace(participant))
        {
            relativePath += $"&participant={Uri.EscapeDataString(participant)}";
        }

        if (nonZeroDeltaOnly)
        {
            relativePath += "&nonZeroDeltaOnly=true";
        }

        return new Uri(httpClient.BaseAddress!, relativePath).ToString();
    }
}
