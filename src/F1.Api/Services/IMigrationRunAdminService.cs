using F1.Api.Dtos;

namespace F1.Api.Services;

public sealed record MigrationRunListQuery(
    int Page,
    int PageSize,
    string? Status,
    DateTime? StartedFromUtc,
    DateTime? StartedToUtc);

public sealed record MigrationRunDiffExportResponse(
    bool Success,
    string? Error,
    string FileName,
    string ContentType,
    byte[] Payload);

public sealed record MigrationRunKickoffCommand(
    string? SourceFilePath,
    string RequestedMode,
    string RequestedBy);

public sealed record MigrationRunKickoffResult(
    bool Success,
    bool Conflict,
    string? Error,
    Guid? ExistingRunId,
    AdminMigrationRunKickoffResponseDto? Run);

public interface IMigrationRunAdminService
{
    Task<AdminMigrationRunListResponseDto> GetRunsAsync(MigrationRunListQuery query, CancellationToken cancellationToken);

    Task<AdminMigrationRunDetailResponseDto?> GetRunDetailAsync(
        Guid runId,
        string requestedBy,
        CancellationToken cancellationToken = default,
        string? expectedStatus = null);

    Task<MigrationRunDiffExportResponse?> ExportRunDiffsAsync(
        Guid runId,
        string exportType,
        string format,
        string requestedBy,
        CancellationToken cancellationToken = default,
        string? expectedStatus = null);

    Task<MigrationRunKickoffResult> KickoffRunAsync(
        MigrationRunKickoffCommand command,
        CancellationToken cancellationToken);
}