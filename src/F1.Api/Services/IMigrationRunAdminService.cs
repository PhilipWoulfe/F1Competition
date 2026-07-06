using F1.Api.Dtos;

namespace F1.Api.Services;

public sealed record MigrationRunListQuery(
    int Page,
    int PageSize,
    string? Status,
    DateTime? StartedFromUtc,
    DateTime? StartedToUtc);

public interface IMigrationRunAdminService
{
    Task<AdminMigrationRunListResponseDto> GetRunsAsync(MigrationRunListQuery query, CancellationToken cancellationToken);

    Task<AdminMigrationRunDetailResponseDto?> GetRunDetailAsync(Guid runId, CancellationToken cancellationToken);
}