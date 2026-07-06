using F1.Api.Dtos;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace F1.Api.Services;

public sealed class MigrationRunAdminService : IMigrationRunAdminService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly F1DbContext _dbContext;
    private readonly ILogger<MigrationRunAdminService> _logger;

    public MigrationRunAdminService(F1DbContext dbContext, ILogger<MigrationRunAdminService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AdminMigrationRunListResponseDto> GetRunsAsync(MigrationRunListQuery query, CancellationToken cancellationToken)
    {
        var page = query.Page <= 0 ? DefaultPage : query.Page;
        var pageSize = query.PageSize <= 0
            ? DefaultPageSize
            : Math.Min(query.PageSize, MaxPageSize);

        try
        {
            var runsQuery = _dbContext.MigrationImportRuns.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(query.Status))
            {
                var normalizedStatus = query.Status.Trim();
                runsQuery = runsQuery.Where(x => x.Status == normalizedStatus);
            }

            if (query.StartedFromUtc.HasValue)
            {
                runsQuery = runsQuery.Where(x => x.StartedAtUtc >= query.StartedFromUtc.Value);
            }

            if (query.StartedToUtc.HasValue)
            {
                runsQuery = runsQuery.Where(x => x.StartedAtUtc <= query.StartedToUtc.Value);
            }

            var totalCount = await runsQuery.CountAsync(cancellationToken);
            var pagedRuns = await runsQuery
                .OrderByDescending(x => x.StartedAtUtc)
                .ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToArrayAsync(cancellationToken);

            if (pagedRuns.Length == 0)
            {
                return new AdminMigrationRunListResponseDto(page, pageSize, totalCount, []);
            }

            var runIds = pagedRuns.Select(run => run.Id).ToArray();

            var unresolvedCounts = await _dbContext.MigrationImportUnresolvedTokens
                .AsNoTracking()
                .Where(x => runIds.Contains(x.ImportRunId))
                .GroupBy(x => x.ImportRunId)
                .Select(group => new { RunId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.RunId, x => x.Count, cancellationToken);

            var pickDiffCounts = await _dbContext.MigrationImportPickDiffs
                .AsNoTracking()
                .Where(x => runIds.Contains(x.ImportRunId))
                .GroupBy(x => x.ImportRunId)
                .Select(group => new { RunId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.RunId, x => x.Count, cancellationToken);

            var raceDiffCounts = await _dbContext.MigrationImportRaceDiffs
                .AsNoTracking()
                .Where(x => runIds.Contains(x.ImportRunId))
                .GroupBy(x => x.ImportRunId)
                .Select(group => new { RunId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.RunId, x => x.Count, cancellationToken);

            var totalDeltas = await _dbContext.MigrationImportParticipantDeltaSummaries
                .AsNoTracking()
                .Where(x => runIds.Contains(x.ImportRunId))
                .GroupBy(x => x.ImportRunId)
                .Select(group => new { RunId = group.Key, TotalDelta = group.Sum(item => item.NetDeltaPoints) })
                .ToDictionaryAsync(x => x.RunId, x => x.TotalDelta, cancellationToken);

            var items = pagedRuns
                .Select(run => new AdminMigrationRunListItemDto(
                    run.Id,
                    run.Status,
                    run.IsDryRun,
                    run.SourceFilePath,
                    run.SourceFileChecksum,
                    run.StartedAtUtc,
                    run.FinishedAtUtc,
                    run.RawRowCount,
                    unresolvedCounts.GetValueOrDefault(run.Id, 0),
                    pickDiffCounts.GetValueOrDefault(run.Id, 0),
                    raceDiffCounts.GetValueOrDefault(run.Id, 0),
                    totalDeltas.GetValueOrDefault(run.Id, 0),
                    run.ErrorMessage))
                .ToArray();

            return new AdminMigrationRunListResponseDto(page, pageSize, totalCount, items);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogWarning(ex,
                "Migration run tables are not fully available yet. Returning an empty run list instead of failing.");
            return new AdminMigrationRunListResponseDto(page, pageSize, 0, []);
        }
    }

    public async Task<AdminMigrationRunDetailResponseDto?> GetRunDetailAsync(Guid runId, string requestedBy, CancellationToken cancellationToken)
    {
        try
        {
            var run = await _dbContext.MigrationImportRuns
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);

            if (run is null)
            {
                return null;
            }

            _logger.LogInformation(
                "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc}",
                "view_detail",
                runId,
                requestedBy,
                DateTime.UtcNow);

        var unresolvedTokenSummaryRows = await _dbContext.MigrationImportUnresolvedTokens
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .GroupBy(x => x.RawToken)
            .Select(group => new
            {
                RawToken = group.Key,
                OccurrenceCount = group.Count(),
                FirstRowNumber = group.Min(item => item.RowNumber),
                FirstCreatedAtUtc = group.Min(item => item.CreatedAtUtc)
            })
            .OrderByDescending(x => x.OccurrenceCount)
            .ThenBy(x => x.RawToken)
            .ToArrayAsync(cancellationToken);

        var unresolvedTokenSummary = unresolvedTokenSummaryRows
            .Select(x => new AdminMigrationUnresolvedTokenSummaryDto(
                x.RawToken,
                x.OccurrenceCount,
                x.FirstRowNumber,
                x.FirstCreatedAtUtc))
            .ToArray();

        var participantDeltas = await _dbContext.MigrationImportParticipantDeltaSummaries
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .Select(x => new AdminMigrationParticipantDeltaDto(
                x.Subject,
                x.ImportedTotalPoints,
                x.CalculatedTotalPoints,
                x.NetDeltaPoints,
                x.TopReasonCode,
                x.TopReasonCount))
            .ToArrayAsync(cancellationToken);

        var raceDiffs = await _dbContext.MigrationImportRaceDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceCode)
            .ThenBy(x => x.Subject)
            .Select(x => new AdminMigrationRaceDiffDto(
                x.RaceCode,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation))
            .ToArrayAsync(cancellationToken);

        var pickDiffs = await _dbContext.MigrationImportPickDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceCode)
            .ThenBy(x => x.Subject)
            .ThenBy(x => x.PickType == "1" ? 1 : x.PickType == "2" ? 2 : x.PickType == "3" ? 3 : x.PickType == "DNF" ? 4 : 5)
            .ThenBy(x => x.PickType)
            .Select(x => new AdminMigrationPickDiffDto(
                x.RaceCode,
                x.PickType,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation))
            .ToArrayAsync(cancellationToken);

            return new AdminMigrationRunDetailResponseDto(
                RunId: run.Id,
                Status: run.Status,
                IsDryRun: run.IsDryRun,
                SourceFilePath: run.SourceFilePath,
                SourceFileChecksum: run.SourceFileChecksum,
                StartedAtUtc: run.StartedAtUtc,
                FinishedAtUtc: run.FinishedAtUtc,
                RawRowCount: run.RawRowCount,
                ErrorMessage: run.ErrorMessage,
                UnresolvedTokenCount: unresolvedTokenSummary.Sum(x => x.OccurrenceCount),
                PickDiffCount: pickDiffs.Length,
                RaceDiffCount: raceDiffs.Length,
                TotalDeltaPoints: participantDeltas.Sum(x => x.NetDeltaPoints),
                UnresolvedTokenSummary: unresolvedTokenSummary,
                ParticipantDeltas: participantDeltas,
                RaceDiffs: raceDiffs,
                PickDiffs: pickDiffs);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogWarning(ex,
                "Migration run tables are not fully available yet. Returning null for run detail request {RunId}.",
                runId);
            return null;
        }
    }

    public async Task<MigrationRunDiffExportResponse?> ExportRunDiffsAsync(
        Guid runId,
        string exportType,
        string format,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var runExists = await _dbContext.MigrationImportRuns
            .AsNoTracking()
            .AnyAsync(x => x.Id == runId, cancellationToken);

        if (!runExists)
        {
            return null;
        }

        var normalizedExportType = exportType.Trim().ToLowerInvariant();
        var normalizedFormat = format.Trim().ToLowerInvariant();

        if (normalizedFormat is not ("csv" or "json"))
        {
            return new MigrationRunDiffExportResponse(
                Success: false,
                Error: "format must be either csv or json.",
                FileName: string.Empty,
                ContentType: "text/plain",
                Payload: []);
        }

        return normalizedExportType switch
        {
            "participant-diffs" => await ExportParticipantDiffsAsync(runId, normalizedFormat, requestedBy, cancellationToken),
            "pick-diffs" => await ExportPickDiffsAsync(runId, normalizedFormat, requestedBy, cancellationToken),
            _ => new MigrationRunDiffExportResponse(
                Success: false,
                Error: "exportType must be participant-diffs or pick-diffs.",
                FileName: string.Empty,
                ContentType: "text/plain",
                Payload: [])
        };
    }

    private async Task<MigrationRunDiffExportResponse> ExportParticipantDiffsAsync(
        Guid runId,
        string format,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.MigrationImportParticipantDeltaSummaries
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .Select(x => new
            {
                x.Subject,
                x.ImportedTotalPoints,
                x.CalculatedTotalPoints,
                x.NetDeltaPoints,
                x.TopReasonCode,
                x.TopReasonCount
            })
            .ToArrayAsync(cancellationToken);

        var extension = format == "json" ? "json" : "csv";
        var fileName = $"migration-run-{runId}-participant-diffs.{extension}";

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} format={Format} exportType={ExportType} rowCount={RowCount}",
            "export",
            runId,
            requestedBy,
            DateTime.UtcNow,
            format,
            "participant-diffs",
            rows.Length);

        if (format == "json")
        {
            return new MigrationRunDiffExportResponse(
                Success: true,
                Error: null,
                FileName: fileName,
                ContentType: "application/json",
                Payload: JsonSerializer.SerializeToUtf8Bytes(rows, ExportJsonOptions));
        }

        var csv = new StringBuilder();
        csv.AppendLine("subject,importedTotalPoints,calculatedTotalPoints,netDeltaPoints,topReasonCode,topReasonCount");
        foreach (var row in rows)
        {
            csv.Append(EscapeCsv(row.Subject)).Append(',')
                .Append(row.ImportedTotalPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.CalculatedTotalPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.NetDeltaPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(row.TopReasonCode)).Append(',')
                .Append(row.TopReasonCount.ToString(CultureInfo.InvariantCulture))
                .AppendLine();
        }

        return new MigrationRunDiffExportResponse(
            Success: true,
            Error: null,
            FileName: fileName,
            ContentType: "text/csv",
            Payload: Encoding.UTF8.GetBytes(csv.ToString()));
    }

    private async Task<MigrationRunDiffExportResponse> ExportPickDiffsAsync(
        Guid runId,
        string format,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.MigrationImportPickDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceCode)
            .ThenBy(x => x.Subject)
            .ThenBy(x => x.PickType == "1" ? 1 : x.PickType == "2" ? 2 : x.PickType == "3" ? 3 : x.PickType == "DNF" ? 4 : 5)
            .ThenBy(x => x.PickType)
            .Select(x => new
            {
                x.RaceCode,
                x.PickType,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation
            })
            .ToArrayAsync(cancellationToken);

        var extension = format == "json" ? "json" : "csv";
        var fileName = $"migration-run-{runId}-pick-diffs.{extension}";

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} format={Format} exportType={ExportType} rowCount={RowCount}",
            "export",
            runId,
            requestedBy,
            DateTime.UtcNow,
            format,
            "pick-diffs",
            rows.Length);

        if (format == "json")
        {
            return new MigrationRunDiffExportResponse(
                Success: true,
                Error: null,
                FileName: fileName,
                ContentType: "application/json",
                Payload: JsonSerializer.SerializeToUtf8Bytes(rows, ExportJsonOptions));
        }

        var csv = new StringBuilder();
        csv.AppendLine("raceCode,pickType,subject,importedPoints,calculatedPoints,deltaPoints,reasonCode,explanation");
        foreach (var row in rows)
        {
            csv.Append(EscapeCsv(row.RaceCode)).Append(',')
                .Append(EscapeCsv(row.PickType)).Append(',')
                .Append(EscapeCsv(row.Subject)).Append(',')
                .Append(row.ImportedPoints?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.CalculatedPoints?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.DeltaPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(row.ReasonCode)).Append(',')
                .Append(EscapeCsv(row.Explanation))
                .AppendLine();
        }

        return new MigrationRunDiffExportResponse(
            Success: true,
            Error: null,
            FileName: fileName,
            ContentType: "text/csv",
            Payload: Encoding.UTF8.GetBytes(csv.ToString()));
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}