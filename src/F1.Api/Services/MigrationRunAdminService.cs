using F1.Api.Dtos;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Security.Cryptography;
using System.Globalization;
using System.Data;
using System.Text;
using System.Text.Json;

namespace F1.Api.Services;

public sealed class MigrationRunAdminService : IMigrationRunAdminService
{
    private const string DefaultSourceFilePath = "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv";
    private const string AllowedImportRootPath = "data/imports";
    private const string AllowedTempImportRootPath = "f1-imports";
    private const string StatusQueued = "Queued";
    private const string StatusStarted = "Started";
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

            var preseasonTotalDeltas = await _dbContext.MigrationImportPreseasonParticipantDeltaSummaries
                .AsNoTracking()
                .Where(x => runIds.Contains(x.ImportRunId))
                .GroupBy(x => x.ImportRunId)
                .Select(group => new { RunId = group.Key, TotalDelta = group.Sum(item => item.NetDeltaPoints) })
                .ToDictionaryAsync(x => x.RunId, x => x.TotalDelta, cancellationToken);

            var unexpectedDeltas = await _dbContext.MigrationImportPickDiffs
                .AsNoTracking()
                .Where(x => runIds.Contains(x.ImportRunId) && !x.IsExpectedVariance && x.DeltaPoints != 0)
                .GroupBy(x => x.ImportRunId)
                .Select(group => new { RunId = group.Key, TotalDelta = group.Sum(item => item.DeltaPoints) })
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
                    totalDeltas.GetValueOrDefault(run.Id, 0) + preseasonTotalDeltas.GetValueOrDefault(run.Id, 0),
                    unexpectedDeltas.GetValueOrDefault(run.Id, 0),
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

    public async Task<MigrationRunKickoffResult> KickoffRunAsync(MigrationRunKickoffCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RequestedMode))
        {
            return new MigrationRunKickoffResult(
                Success: false,
                Conflict: false,
                Error: "Mode is required.",
                ExistingRunId: null,
                Run: null);
        }

        var normalizedMode = command.RequestedMode.Trim().ToLowerInvariant();
        if (normalizedMode is not ("dry-run" or "write"))
        {
            return new MigrationRunKickoffResult(
                Success: false,
                Conflict: false,
                Error: "Mode must be dry-run or write.",
                ExistingRunId: null,
                Run: null);
        }

        var requestedSource = string.IsNullOrWhiteSpace(command.SourceFilePath)
            ? DefaultSourceFilePath
            : command.SourceFilePath.Trim();
        var sourceFilePath = ResolveSourceFilePath(requestedSource);
        if (sourceFilePath is null)
        {
            return new MigrationRunKickoffResult(
                Success: false,
                Conflict: false,
                Error: "Source file path must be within the configured import directory.",
                ExistingRunId: null,
                Run: null);
        }

        if (!File.Exists(sourceFilePath))
        {
            return new MigrationRunKickoffResult(
                Success: false,
                Conflict: false,
                Error: "Migration source file was not found.",
                ExistingRunId: null,
                Run: null);
        }

        var checksum = await ComputeSha256Async(sourceFilePath, cancellationToken);
        var now = DateTime.UtcNow;
        var isDryRun = normalizedMode == "dry-run";
        var runId = Guid.NewGuid();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var activeRun = await _dbContext.MigrationImportRuns
                .AsNoTracking()
                .Where(x =>
                    (x.Status == StatusQueued || x.Status == StatusStarted) &&
                    x.FinishedAtUtc == null &&
                    x.SourceFileChecksum == checksum)
                .OrderByDescending(x => x.StartedAtUtc)
                .ThenByDescending(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeRun is not null)
            {
                await transaction.RollbackAsync(cancellationToken);

                return new MigrationRunKickoffResult(
                    Success: false,
                    Conflict: true,
                    Error: "An active migration run already exists for this source/checksum.",
                    ExistingRunId: activeRun.Id,
                    Run: null);
            }

            _dbContext.MigrationImportRuns.Add(new()
            {
                Id = runId,
                SourceFilePath = sourceFilePath,
                SourceFileChecksum = checksum,
                IsDryRun = isDryRun,
                Status = StatusQueued,
                StartedAtUtc = now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure })
        {
            await transaction.RollbackAsync(cancellationToken);
            var existingRunId = await FindActiveRunIdAsync(sourceFilePath, checksum, cancellationToken);

            return new MigrationRunKickoffResult(
                Success: false,
                Conflict: true,
                Error: "An active migration run already exists for this source/checksum.",
                ExistingRunId: existingRunId,
                Run: null);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            var existingRunId = await FindActiveRunIdAsync(sourceFilePath, checksum, cancellationToken);

            return new MigrationRunKickoffResult(
                Success: false,
                Conflict: true,
                Error: "An active migration run already exists for this source/checksum.",
                ExistingRunId: existingRunId,
                Run: null);
        }

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} requestedMode={RequestedMode} sourceFilePath={SourceFilePath} checksum={Checksum}",
            "kickoff",
            runId,
            command.RequestedBy,
            now,
            normalizedMode,
            sourceFilePath,
            checksum);

        return new MigrationRunKickoffResult(
            Success: true,
            Conflict: false,
            Error: null,
            ExistingRunId: null,
            Run: new AdminMigrationRunKickoffResponseDto(
                RunId: runId,
                Status: StatusQueued,
                IsDryRun: isDryRun,
                RequestedMode: normalizedMode,
                SourceFilePath: sourceFilePath,
                SourceFileChecksum: checksum,
                TriggeredAtUtc: now,
                RequestedBy: command.RequestedBy));
    }

    public async Task<AdminMigrationRunDetailResponseDto?> GetRunDetailAsync(
        Guid runId,
        string requestedBy,
        CancellationToken cancellationToken,
        string? expectedStatus)
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

        var allPickDiffs = await _dbContext.MigrationImportPickDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Id)
            .Select(x => new AdminMigrationPickDiffDto(
                x.RaceCode,
                x.PickType,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation,
                x.IsExpectedVariance,
                x.ExpectedVarianceReasonCode,
                x.ExpectedVarianceRuleId))
            .ToArrayAsync(cancellationToken);

        var allRaceDiffs = await _dbContext.MigrationImportRaceDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Id)
            .Select(x => new AdminMigrationRaceDiffDto(
                x.RaceCode,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation,
                x.IsExpectedVariance,
                x.ExpectedVarianceReasonCode,
                x.ExpectedVarianceRuleId))
            .ToArrayAsync(cancellationToken);

        var pickDiffs = FilterExpectedVariance(allPickDiffs, expectedStatus).ToArray();
        var raceDiffs = FilterExpectedVariance(allRaceDiffs, expectedStatus).ToArray();
        var unexpectedTotalDeltaPoints = allPickDiffs
            .Where(x => !x.IsExpectedVariance && x.DeltaPoints != 0)
            .Sum(x => x.DeltaPoints);

        var participantDeltas = raceDiffs
            .GroupBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var topReasonGroup = pickDiffs
                    .Where(x => string.Equals(x.Subject, group.Key, StringComparison.OrdinalIgnoreCase) && x.DeltaPoints != 0)
                    .GroupBy(x => x.ReasonCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                return new AdminMigrationParticipantDeltaDto(
                    group.Key,
                    group.Sum(x => x.ImportedPoints),
                    group.Sum(x => x.CalculatedPoints),
                    group.Sum(x => x.DeltaPoints),
                    topReasonGroup?.Key,
                    topReasonGroup?.Count() ?? 0);
            })
            .ToArray();

        var preseasonQuestionDiffs = await _dbContext.MigrationImportPreseasonQuestionDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Subject)
            .ThenBy(x => x.QuestionKey)
            .Select(x => new AdminMigrationPreseasonQuestionDiffDto(
                x.RowNumber,
                x.QuestionKey,
                x.QuestionText,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation))
            .ToArrayAsync(cancellationToken);

        var preseasonParticipantDeltas = await _dbContext.MigrationImportPreseasonParticipantDeltaSummaries
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .Select(x => new AdminMigrationPreseasonParticipantDeltaDto(
                x.Subject,
                x.ImportedTotalPoints,
                x.CalculatedTotalPoints,
                x.NetDeltaPoints,
                x.TopReasonCode,
                x.TopReasonCount))
            .ToArrayAsync(cancellationToken);

        var preseasonReasonCategorySummaries = await _dbContext.MigrationImportPreseasonReasonCategorySummaries
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderByDescending(x => x.OccurrenceCount)
            .ThenBy(x => x.ReasonCode)
            .Select(x => new AdminMigrationPreseasonReasonCategorySummaryDto(
                x.ReasonCode,
                x.OccurrenceCount,
                x.TotalDeltaPoints))
            .ToArrayAsync(cancellationToken);

        var preseasonSummary = new AdminMigrationPreseasonSummaryDto(
            QuestionDiffCount: preseasonQuestionDiffs.Length,
            ParticipantDeltaCount: preseasonParticipantDeltas.Length,
            ReasonCategoryCount: preseasonReasonCategorySummaries.Length,
            TotalDeltaPoints: preseasonQuestionDiffs.Sum(x => x.DeltaPoints));

            var raceTotalDeltaPoints = allPickDiffs.Sum(x => x.DeltaPoints);
            var preseasonTotalDeltaPoints = preseasonSummary.TotalDeltaPoints;

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
                TotalDeltaPoints: raceTotalDeltaPoints + preseasonTotalDeltaPoints,
                UnexpectedTotalDeltaPoints: unexpectedTotalDeltaPoints,
                UnresolvedTokenSummary: unresolvedTokenSummary,
                ParticipantDeltas: participantDeltas,
                PreseasonSummary: preseasonSummary,
                PreseasonParticipantDeltas: preseasonParticipantDeltas,
                PreseasonQuestionDiffs: preseasonQuestionDiffs,
                PreseasonReasonCategorySummaries: preseasonReasonCategorySummaries,
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
        CancellationToken cancellationToken = default,
        string? expectedStatus = null,
        string? category = null,
        string? participant = null,
        bool nonZeroDeltaOnly = false)
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
            "participant-diffs" => await ExportParticipantDiffsAsync(runId, normalizedFormat, requestedBy, cancellationToken, expectedStatus),
            "pick-diffs" => await ExportPickDiffsAsync(runId, normalizedFormat, requestedBy, cancellationToken, expectedStatus),
            "question-diffs" => await ExportQuestionDiffsAsync(runId, normalizedFormat, requestedBy, cancellationToken, category, participant, expectedStatus, nonZeroDeltaOnly),
            "preseason-question-diffs" => await ExportPreseasonQuestionDiffsAsync(runId, normalizedFormat, requestedBy, cancellationToken),
            "preseason-participant-diffs" => await ExportPreseasonParticipantDiffsAsync(runId, normalizedFormat, requestedBy, cancellationToken),
            _ => new MigrationRunDiffExportResponse(
                Success: false,
                Error: "exportType must be participant-diffs, pick-diffs, question-diffs, preseason-question-diffs, or preseason-participant-diffs.",
                FileName: string.Empty,
                ContentType: "text/plain",
                Payload: [])
        };
    }

    public async Task<AdminMigrationQuestionDiffListResponseDto?> GetQuestionDiffsAsync(
        Guid runId,
        int page,
        int pageSize,
        string requestedBy,
        CancellationToken cancellationToken = default,
        string? category = null,
        string? participant = null,
        string? expectedStatus = null,
        bool nonZeroDeltaOnly = false)
    {
        var runExists = await _dbContext.MigrationImportRuns
            .AsNoTracking()
            .AnyAsync(x => x.Id == runId, cancellationToken);

        if (!runExists)
        {
            return null;
        }

        var normalizedPage = page <= 0 ? DefaultPage : page;
        var normalizedPageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} category={Category} participant={Participant} expectedStatus={ExpectedStatus} nonZeroDeltaOnly={NonZeroDeltaOnly} page={Page} pageSize={PageSize}",
            "view_question_diffs",
            runId,
            requestedBy,
            DateTime.UtcNow,
            category,
            participant,
            expectedStatus,
            nonZeroDeltaOnly,
            normalizedPage,
            normalizedPageSize);

        var allRows = await BuildQuestionDiffRowsAsync(runId, cancellationToken);
        var filteredRows = ApplyQuestionFilters(allRows, category, participant, expectedStatus, nonZeroDeltaOnly)
            .ToArray();

        var pagedRows = filteredRows
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToArray();

        return new AdminMigrationQuestionDiffListResponseDto(
            Page: normalizedPage,
            PageSize: normalizedPageSize,
            TotalCount: filteredRows.Length,
            Items: pagedRows);
    }

    public async Task<AdminMigrationQuestionDiffSummaryResponseDto?> GetQuestionDiffSummaryAsync(
        Guid runId,
        string requestedBy,
        CancellationToken cancellationToken = default,
        string? category = null,
        string? participant = null,
        string? expectedStatus = null,
        bool nonZeroDeltaOnly = false)
    {
        var runExists = await _dbContext.MigrationImportRuns
            .AsNoTracking()
            .AnyAsync(x => x.Id == runId, cancellationToken);

        if (!runExists)
        {
            return null;
        }

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} category={Category} participant={Participant} expectedStatus={ExpectedStatus} nonZeroDeltaOnly={NonZeroDeltaOnly}",
            "view_question_summary",
            runId,
            requestedBy,
            DateTime.UtcNow,
            category,
            participant,
            expectedStatus,
            nonZeroDeltaOnly);

        var allRows = await BuildQuestionDiffRowsAsync(runId, cancellationToken);
        var filteredRows = ApplyQuestionFilters(allRows, category, participant, expectedStatus, nonZeroDeltaOnly)
            .ToArray();

        var categorySummary = filteredRows
            .GroupBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AdminMigrationQuestionDiffCategorySummaryDto(
                Category: group.Key,
                Count: group.Count(),
                TotalDeltaPoints: group.Sum(x => x.DeltaPoints)))
            .ToArray();

        return new AdminMigrationQuestionDiffSummaryResponseDto(
            TotalCount: filteredRows.Length,
            NonZeroDeltaCount: filteredRows.Count(x => x.DeltaPoints != 0),
            TotalDeltaPoints: filteredRows.Sum(x => x.DeltaPoints),
            Categories: categorySummary);
    }

    private async Task<MigrationRunDiffExportResponse> ExportQuestionDiffsAsync(
        Guid runId,
        string format,
        string requestedBy,
        CancellationToken cancellationToken,
        string? category,
        string? participant,
        string? expectedStatus,
        bool nonZeroDeltaOnly)
    {
        var rows = ApplyQuestionFilters(
                await BuildQuestionDiffRowsAsync(runId, cancellationToken),
                category,
                participant,
                expectedStatus,
                nonZeroDeltaOnly)
            .ToArray();

        var extension = format == "json" ? "json" : "csv";
        var fileName = $"migration-run-{runId}-question-diffs.{extension}";

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} format={Format} exportType={ExportType} rowCount={RowCount}",
            "export",
            runId,
            requestedBy,
            DateTime.UtcNow,
            format,
            "question-diffs",
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
        csv.AppendLine("category,questionId,questionText,participant,importedPoints,calculatedPoints,deltaPoints,reasonCode");
        foreach (var row in rows)
        {
            csv.Append(EscapeCsv(row.Category)).Append(',')
                .Append(EscapeCsv(row.QuestionId)).Append(',')
                .Append(EscapeCsv(row.QuestionText)).Append(',')
                .Append(EscapeCsv(row.Participant)).Append(',')
                .Append(row.ImportedPoints?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.CalculatedPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.DeltaPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(row.ReasonCode))
                .AppendLine();
        }

        return new MigrationRunDiffExportResponse(
            Success: true,
            Error: null,
            FileName: fileName,
            ContentType: "text/csv",
            Payload: Encoding.UTF8.GetBytes(csv.ToString()));
    }

    private async Task<AdminMigrationQuestionDiffDto[]> BuildQuestionDiffRowsAsync(Guid runId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.QuestionScores
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .Join(
                _dbContext.QuestionTemplates.AsNoTracking(),
                score => score.QuestionTemplateId,
                template => template.Id,
                (score, template) => new
                {
                    template.Category,
                    template.QuestionId,
                    template.Prompt,
                    score.ParticipantId,
                    score.ImportedPoints,
                    score.CalculatedPoints,
                    score.DeltaPoints,
                    score.ReasonCode
                })
            .OrderBy(x => x.Category)
            .ThenBy(x => x.QuestionId)
            .ThenBy(x => x.ParticipantId)
            .ThenBy(x => x.ReasonCode)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(x => new AdminMigrationQuestionDiffDto(
                x.Category.ToString(),
                x.QuestionId,
                x.Prompt,
                x.ParticipantId,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode))
            .ToArray();
    }

    private static IEnumerable<AdminMigrationQuestionDiffDto> ApplyQuestionFilters(
        IEnumerable<AdminMigrationQuestionDiffDto> rows,
        string? category,
        string? participant,
        string? expectedStatus,
        bool nonZeroDeltaOnly)
    {
        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(category, "all", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(x => string.Equals(x.Category, category.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(participant))
        {
            var participantFilter = participant.Trim();
            rows = rows.Where(x => x.Participant.Contains(participantFilter, StringComparison.OrdinalIgnoreCase));
        }

        if (nonZeroDeltaOnly)
        {
            rows = rows.Where(x => x.DeltaPoints != 0);
        }

        if (string.Equals(expectedStatus, "expected", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(x => x.DeltaPoints == 0);
        }
        else if (string.Equals(expectedStatus, "unexpected", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(x => x.DeltaPoints != 0);
        }

        return rows;
    }

    private async Task<MigrationRunDiffExportResponse> ExportPreseasonQuestionDiffsAsync(
        Guid runId,
        string format,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.MigrationImportPreseasonQuestionDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Subject)
            .ThenBy(x => x.QuestionKey)
            .Select(x => new AdminMigrationPreseasonQuestionDiffDto(
                x.RowNumber,
                x.QuestionKey,
                x.QuestionText,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation))
            .ToArrayAsync(cancellationToken);

        var extension = format == "json" ? "json" : "csv";
        var fileName = $"migration-run-{runId}-preseason-question-diffs.{extension}";

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} format={Format} exportType={ExportType} rowCount={RowCount}",
            "export",
            runId,
            requestedBy,
            DateTime.UtcNow,
            format,
            "preseason-question-diffs",
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
        csv.AppendLine("rowNumber,questionKey,questionText,subject,importedPoints,calculatedPoints,deltaPoints,reasonCode,explanation");
        foreach (var row in rows)
        {
            csv.Append(row.RowNumber.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(row.QuestionKey)).Append(',')
                .Append(EscapeCsv(row.QuestionText)).Append(',')
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

    private async Task<MigrationRunDiffExportResponse> ExportPreseasonParticipantDiffsAsync(
        Guid runId,
        string format,
        string requestedBy,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.MigrationImportPreseasonParticipantDeltaSummaries
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .Select(x => new AdminMigrationPreseasonParticipantDeltaDto(
                x.Subject,
                x.ImportedTotalPoints,
                x.CalculatedTotalPoints,
                x.NetDeltaPoints,
                x.TopReasonCode,
                x.TopReasonCount))
            .ToArrayAsync(cancellationToken);

        var extension = format == "json" ? "json" : "csv";
        var fileName = $"migration-run-{runId}-preseason-participant-diffs.{extension}";

        _logger.LogInformation(
            "MigrationRunAdminAudit action={Action} runId={RunId} requestedBy={RequestedBy} timestampUtc={TimestampUtc} format={Format} exportType={ExportType} rowCount={RowCount}",
            "export",
            runId,
            requestedBy,
            DateTime.UtcNow,
            format,
            "preseason-participant-diffs",
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

    private async Task<MigrationRunDiffExportResponse> ExportParticipantDiffsAsync(
        Guid runId,
        string format,
        string requestedBy,
        CancellationToken cancellationToken,
        string? expectedStatus)
    {
        var rows = await BuildParticipantDiffRowsAsync(runId, expectedStatus, cancellationToken);

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
        CancellationToken cancellationToken,
        string? expectedStatus)
    {
        var rows = await _dbContext.MigrationImportPickDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Id)
            .Select(x => new AdminMigrationPickDiffDto(
                x.RaceCode,
                x.PickType,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation,
                x.IsExpectedVariance,
                x.ExpectedVarianceReasonCode,
                x.ExpectedVarianceRuleId))
            .ToArrayAsync(cancellationToken);

        rows = FilterExpectedVariance(rows, expectedStatus).ToArray();

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
        csv.AppendLine("raceCode,pickType,subject,importedPoints,calculatedPoints,deltaPoints,reasonCode,isExpectedVariance,expectedVarianceReasonCode,expectedVarianceRuleId,explanation");
        foreach (var row in rows)
        {
            csv.Append(EscapeCsv(row.RaceCode)).Append(',')
                .Append(EscapeCsv(row.PickType)).Append(',')
                .Append(EscapeCsv(row.Subject)).Append(',')
                .Append(row.ImportedPoints?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.CalculatedPoints?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.DeltaPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(EscapeCsv(row.ReasonCode)).Append(',')
                .Append(row.IsExpectedVariance.ToString()).Append(',')
                .Append(EscapeCsv(row.ExpectedVarianceReasonCode)).Append(',')
                .Append(EscapeCsv(row.ExpectedVarianceRuleId)).Append(',')
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

    private static bool? ResolveExpectedVarianceFilter(string? expectedStatus)
    {
        if (string.IsNullOrWhiteSpace(expectedStatus) || string.Equals(expectedStatus, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.Equals(expectedStatus, "expected", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(expectedStatus, "unexpected", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static IEnumerable<T> FilterExpectedVariance<T>(IEnumerable<T> rows, string? expectedStatus)
        where T : class
    {
        var includeExpected = ResolveExpectedVarianceFilter(expectedStatus);
        if (includeExpected is null)
        {
            return rows;
        }

        return rows.Where(row =>
        {
            var (isExpectedVariance, deltaPoints) = row switch
            {
                AdminMigrationPickDiffDto pickDiff => (pickDiff.IsExpectedVariance, pickDiff.DeltaPoints),
                AdminMigrationRaceDiffDto raceDiff => (raceDiff.IsExpectedVariance, raceDiff.DeltaPoints),
                _ => (false, 0)
            };

            // Zero-delta rows are not variances and should not appear in expected/unexpected-only views.
            if (deltaPoints == 0)
            {
                return false;
            }

            return includeExpected.Value == isExpectedVariance;
        });
    }

    private async Task<AdminMigrationParticipantDeltaDto[]> BuildParticipantDiffRowsAsync(Guid runId, string? expectedStatus, CancellationToken cancellationToken)
    {
        var pickDiffs = await _dbContext.MigrationImportPickDiffs
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Id)
            .Select(x => new AdminMigrationPickDiffDto(
                x.RaceCode,
                x.PickType,
                x.Subject,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints,
                x.ReasonCode,
                x.Explanation,
                x.IsExpectedVariance,
                x.ExpectedVarianceReasonCode,
                x.ExpectedVarianceRuleId))
            .ToArrayAsync(cancellationToken);

        var filteredPickDiffs = FilterExpectedVariance(pickDiffs, expectedStatus);

        return filteredPickDiffs
            .GroupBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var topReasonGroup = group
                    .Where(x => x.DeltaPoints != 0)
                    .GroupBy(x => x.ReasonCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                return new AdminMigrationParticipantDeltaDto(
                    group.Key,
                    group.Sum(x => x.ImportedPoints ?? 0),
                    group.Sum(x => x.CalculatedPoints ?? 0),
                    group.Sum(x => x.DeltaPoints),
                    topReasonGroup?.Key,
                    topReasonGroup?.Count() ?? 0);
            })
            .ToArray();
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

    private async Task<Guid?> FindActiveRunIdAsync(string sourceFilePath, string checksum, CancellationToken cancellationToken)
    {
        return await _dbContext.MigrationImportRuns
            .AsNoTracking()
            .Where(x =>
                (x.Status == StatusQueued || x.Status == StatusStarted) &&
                x.FinishedAtUtc == null &&
                x.SourceFileChecksum == checksum)
            .OrderByDescending(x => x.StartedAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static string? ResolveSourceFilePath(string sourceFilePath)
    {
        var candidatePath = Path.GetFullPath(sourceFilePath, Directory.GetCurrentDirectory());
        var importRoot = Path.GetFullPath(AllowedImportRootPath, Directory.GetCurrentDirectory());
        var tempImportRoot = Path.GetFullPath(AllowedTempImportRootPath, Path.GetTempPath());

        if (IsPathWithinRoot(candidatePath, importRoot) || IsPathWithinRoot(candidatePath, tempImportRoot))
        {
            return candidatePath;
        }

        return null;
    }

    private static bool IsPathWithinRoot(string candidatePath, string rootPath)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.Equals(candidatePath, rootPath, comparison))
        {
            return true;
        }

        var rootWithSeparator = rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + Path.DirectorySeparatorChar;

        return candidatePath.StartsWith(rootWithSeparator, comparison);
    }

    private static async Task<string> ComputeSha256Async(string sourceFilePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(sourceFilePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        var builder = new StringBuilder(hash.Length * 2);

        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }
}