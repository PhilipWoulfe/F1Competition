using F1.Web.Models;

namespace F1.Web.Services.Api;

public interface IMigrationRunsApiService
{
    Task<AdminMigrationRunListResponse> GetRunsAsync(
        int page,
        int pageSize,
        string? status = null,
        DateTime? startedFromUtc = null,
        DateTime? startedToUtc = null,
        CancellationToken cancellationToken = default);

    Task<AdminMigrationRunDetailResponse?> GetRunDetailAsync(
        Guid runId,
        CancellationToken cancellationToken = default,
        string? expectedStatus = null);

    Task<AdminMigrationQuestionDiffListResponse?> GetQuestionDiffsAsync(
        Guid runId,
        int page,
        int pageSize,
        string? category = null,
        string? participant = null,
        string? expectedStatus = null,
        bool nonZeroDeltaOnly = false,
        CancellationToken cancellationToken = default);

    Task<AdminMigrationQuestionDiffSummaryResponse?> GetQuestionSummaryAsync(
        Guid runId,
        string? category = null,
        string? participant = null,
        string? expectedStatus = null,
        bool nonZeroDeltaOnly = false,
        CancellationToken cancellationToken = default);

    Task<AdminMigrationRunKickoffResponse> StartRunAsync(AdminMigrationRunKickoffRequest request, CancellationToken cancellationToken = default);

    Task<AdminMigrationRunKickoffResponse> StartRunFromUploadAsync(AdminMigrationRunKickoffUploadRequest request, CancellationToken cancellationToken = default);

    string GetRunDiffExportUrl(
        Guid runId,
        string exportType,
        string format,
        string? expectedStatus = null,
        string? category = null,
        string? participant = null,
        bool nonZeroDeltaOnly = false);
}
