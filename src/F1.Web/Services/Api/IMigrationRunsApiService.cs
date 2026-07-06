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

    Task<AdminMigrationRunDetailResponse?> GetRunDetailAsync(Guid runId, CancellationToken cancellationToken = default);
}
