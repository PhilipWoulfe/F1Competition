using F1.Api.Dtos;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
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
    private const string NonEmptyDbStrategy = "merge_upsert_active_records";
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

            var rawRowFallbackCounts = await _dbContext.MigrationImportRawRows
                .AsNoTracking()
                .Where(x => runIds.Contains(x.ImportRunId))
                .GroupBy(x => x.ImportRunId)
                .Select(group => new { RunId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(x => x.RunId, x => x.Count, cancellationToken);

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
                    run.RawRowCount > 0 ? run.RawRowCount : rawRowFallbackCounts.GetValueOrDefault(run.Id, 0),
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

        var existingDriverCount = await _dbContext.Drivers.CountAsync(cancellationToken);
        var existingRaceCount = await _dbContext.Races.CountAsync(cancellationToken);
        var existingSelectionCount = await _dbContext.Selections.CountAsync(cancellationToken);
        var canonicalDataPresent = existingDriverCount > 0 || existingRaceCount > 0 || existingSelectionCount > 0;

        var estimatedAffectedRaceCount = await EstimateAffectedRaceCountAsync(sourceFilePath, cancellationToken);
        var estimatedAffectedParticipantCount = await EstimateAffectedParticipantCountAsync(sourceFilePath, cancellationToken);
        var estimatedAffectedSelectionCount = estimatedAffectedRaceCount * estimatedAffectedParticipantCount;

        if (!isDryRun && canonicalDataPresent && !command.ConfirmNonEmptyStrategy)
        {
            return new MigrationRunKickoffResult(
                Success: false,
                Conflict: false,
                Error: "Write mode requires non-empty DB strategy confirmation. Re-submit with confirmNonEmptyStrategy=true.",
                ExistingRunId: null,
                Run: null);
        }

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
                RequestedBy: command.RequestedBy,
                NonEmptyDbStrategy: NonEmptyDbStrategy,
                CanonicalDataPresent: canonicalDataPresent,
                ExistingDriverCount: existingDriverCount,
                ExistingRaceCount: existingRaceCount,
                ExistingSelectionCount: existingSelectionCount,
                EstimatedAffectedRaceCount: estimatedAffectedRaceCount,
                EstimatedAffectedParticipantCount: estimatedAffectedParticipantCount,
                EstimatedAffectedSelectionCount: estimatedAffectedSelectionCount));
    }

    public async Task<MigrationRunRollbackResult> RollbackRunAsync(MigrationRunRollbackCommand command, CancellationToken cancellationToken)
    {
        var run = await _dbContext.MigrationImportRuns
            .FirstOrDefaultAsync(x => x.Id == command.RunId, cancellationToken);

        if (run is null)
        {
            return new MigrationRunRollbackResult(false, "Migration run was not found.", null);
        }

        if (!string.Equals(run.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(run.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return new MigrationRunRollbackResult(false, "Only completed or failed runs can be rolled back.", null);
        }

        var raceCodes = await _dbContext.MigrationImportRaceSelections
            .AsNoTracking()
            .Where(x => x.ImportRunId == command.RunId && !x.IsActualOutcome)
            .Select(x => x.RaceCode)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var rollbackSeasons = await _dbContext.MigrationImportRaceRoundMappings
            .AsNoTracking()
            .Where(x => x.ImportRunId == command.RunId && x.Season.HasValue)
            .Select(x => x.Season!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (rollbackSeasons.Length == 0)
        {
            return new MigrationRunRollbackResult(
                false,
                "Unable to determine migration season for rollback scope.",
                null);
        }

        if (rollbackSeasons.Length > 1)
        {
            return new MigrationRunRollbackResult(
                false,
                "Rollback scope is ambiguous because the run contains multiple seasons.",
                null);
        }

        var raceIds = await _dbContext.Races
            .AsNoTracking()
            .Where(x => x.Season == rollbackSeasons[0] && raceCodes.Contains(x.CircuitName))
            .Select(x => x.Id)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        var selectionIds = await _dbContext.Selections
            .AsNoTracking()
            .Where(x => raceIds.Contains(x.RaceId))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var racePickScores = await _dbContext.RacePickScores
                .Where(x => raceIds.Contains(x.RaceId))
                .ToListAsync(cancellationToken);
            var selectionPositions = await _dbContext.SelectionPositions
                .Where(x => selectionIds.Contains(x.SelectionId))
                .ToListAsync(cancellationToken);
            var selections = await _dbContext.Selections
                .Where(x => selectionIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
            var races = await _dbContext.Races
                .Where(x => raceIds.Contains(x.Id))
                .ToListAsync(cancellationToken);

            var affectedSelectionPositionCount = selectionPositions.Count;
            var affectedSelectionCount = selections.Count;
            var affectedRaceCount = races.Count;

            _dbContext.RacePickScores.RemoveRange(racePickScores);
            _dbContext.SelectionPositions.RemoveRange(selectionPositions);
            _dbContext.Selections.RemoveRange(selections);
            _dbContext.Races.RemoveRange(races);

            var requestedAtUtc = DateTime.UtcNow;
            _dbContext.MigrationImportRollbackAudits.Add(new MigrationImportRollbackAuditEntity
            {
                ImportRunId = command.RunId,
                Actor = command.RequestedBy,
                Reason = command.Reason,
                RequestedAtUtc = requestedAtUtc,
                AffectedRaceCount = affectedRaceCount,
                AffectedSelectionCount = affectedSelectionCount,
                AffectedSelectionPositionCount = affectedSelectionPositionCount,
                Outcome = "Completed"
            });

            run.Status = "RolledBack";
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new MigrationRunRollbackResult(
                true,
                null,
                new AdminMigrationRollbackResponseDto(
                    command.RunId,
                    run.Status,
                    requestedAtUtc,
                    command.RequestedBy,
                    "Completed",
                    affectedRaceCount,
                    affectedSelectionCount,
                    affectedSelectionPositionCount));
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new MigrationRunRollbackResult(false, ex.Message, null);
        }
    }

    private static async Task<int> EstimateAffectedRaceCountAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        var raceCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var stream = File.OpenRead(sourceFilePath);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split(',');
            if (columns.Length == 0)
            {
                continue;
            }

            var label = columns[0].Trim();
            if (label.Length < 4)
            {
                continue;
            }

            var dashIndex = label.IndexOf('-');
            if (dashIndex <= 0)
            {
                continue;
            }

            var suffix = label[(dashIndex + 1)..].Trim();
            if (!suffix.Equals("1", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            raceCodes.Add(label[..dashIndex]);
        }

        return raceCodes.Count;
    }

    private static async Task<int> EstimateAffectedParticipantCountAsync(string sourceFilePath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(sourceFilePath);
        using var reader = new StreamReader(stream);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var columns = line.Split(',');
            if (columns.Length < 2)
            {
                continue;
            }

            if (!string.Equals(columns[0].Trim(), "Question", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var participants = columns
                .Skip(1)
                .TakeWhile(x => !string.IsNullOrWhiteSpace(x))
                .Count();
            return participants;
        }

        return 0;
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

        var rawRowFallbackCount = run.RawRowCount > 0
            ? run.RawRowCount
            : await _dbContext.MigrationImportRawRows
                .AsNoTracking()
                .CountAsync(x => x.ImportRunId == runId, cancellationToken);

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

        var conflictDiagnostics = await _dbContext.MigrationImportConflictDiagnostics
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.EntityType)
            .Select(x => new AdminMigrationConflictDiagnosticDto(
                x.EntityType,
                x.ConflictType,
                x.KeyFields,
                x.SourceReference,
                x.PolicyOutcome,
                x.RecommendedAction,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken);

        var rollbackAudits = await _dbContext.MigrationImportRollbackAudits
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => new AdminMigrationRollbackAuditDto(
                x.RequestedAtUtc,
                x.Actor,
                x.Reason,
                x.Outcome,
                x.AffectedRaceCount,
                x.AffectedSelectionCount,
                x.AffectedSelectionPositionCount))
            .ToArrayAsync(cancellationToken);

        var preseasonSummary = new AdminMigrationPreseasonSummaryDto(
            QuestionDiffCount: preseasonQuestionDiffs.Length,
            ParticipantDeltaCount: preseasonParticipantDeltas.Length,
            ReasonCategoryCount: preseasonReasonCategorySummaries.Length,
            TotalDeltaPoints: preseasonQuestionDiffs.Sum(x => x.DeltaPoints));

        var participantComponentDeltas = BuildParticipantComponentDeltas(pickDiffs, preseasonQuestionDiffs);
        var cdpParity = await BuildCdpParityAsync(run.Id, cancellationToken);
        var sourceManifest = await BuildSourceManifestAsync(run.Id, cancellationToken);
        var sourceContractDiagnostics = BuildSourceContractDiagnostics(run.SourceFilePath, sourceManifest);

        var isDaveProfile = IsDaveSourcePath(run.SourceFilePath);
        var (h2hPointsPolicy, preseasonPointsPolicy) = await ResolvePolicySummaryAsync(run.Id, isDaveProfile, cancellationToken);
        var raceBonusModes = await ResolveRaceBonusModesAsync(run.Id, isDaveProfile, cancellationToken);

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
            RawRowCount: rawRowFallbackCount,
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
            PickDiffs: pickDiffs,
            ParticipantComponentDeltas: participantComponentDeltas,
            CdpParity: cdpParity,
            SourceManifest: sourceManifest,
            SourceContractDiagnostics: sourceContractDiagnostics,
            H2hPointsPolicy: h2hPointsPolicy,
            PreseasonPointsPolicy: preseasonPointsPolicy,
            RaceBonusModes: raceBonusModes,
            ConflictDiagnostics: conflictDiagnostics,
            RollbackAudits: rollbackAudits);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogWarning(ex,
                "Migration run tables are not fully available yet. Returning null for run detail request {RunId}.",
                runId);
            return null;
        }
    }

    private async Task<(int? H2hPointsPolicy, int? PreseasonPointsPolicy)> ResolvePolicySummaryAsync(
        Guid runId,
        bool isDaveProfile,
        CancellationToken cancellationToken)
    {
        var preseasonPoints = await _dbContext.MigrationImportPreseasonPolicies
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId && x.PointsPerQuestion.HasValue)
            .OrderByDescending(x => x.RowNumber)
            .Select(x => x.PointsPerQuestion)
            .FirstOrDefaultAsync(cancellationToken);

        if (!preseasonPoints.HasValue)
        {
            preseasonPoints = isDaveProfile ? 30 : 20;
        }

        var h2hPoints = isDaveProfile ? 5 : 1;
        return (h2hPoints, preseasonPoints);
    }

    private async Task<IReadOnlyList<AdminMigrationRaceBonusModeDto>> ResolveRaceBonusModesAsync(
        Guid runId,
        bool isDaveProfile,
        CancellationToken cancellationToken)
    {
        var seasonQuestionRows = await _dbContext.MigrationImportRawRows
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId && x.SectionType == "SeasonQuestionPrediction")
            .OrderBy(x => x.RowNumber)
            .ToListAsync(cancellationToken);

        if (seasonQuestionRows.Count == 0)
        {
            return [];
        }

        var raceBonusQuestionIds = seasonQuestionRows
            .Where(row => IsRaceBonusPrompt(row.RawPayload))
            .Select(row => $"PRE-{row.RowNumber:D3}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (raceBonusQuestionIds.Length == 0)
        {
            return [];
        }

        var templates = await _dbContext.QuestionTemplates
            .AsNoTracking()
            .Where(x =>
                x.Category == QuestionCategory.RaceBonus &&
                raceBonusQuestionIds.Contains(x.QuestionId) &&
                x.Season == 2025)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.QuestionId)
            .ToListAsync(cancellationToken);

        var result = new List<AdminMigrationRaceBonusModeDto>(templates.Count);
        foreach (var template in templates)
        {
            var options = ParseRaceBonusOptions(template.OptionsJson, isDaveProfile);
            result.Add(new AdminMigrationRaceBonusModeDto(
                QuestionId: template.QuestionId,
                Prompt: template.Prompt,
                Mode: options.Mode,
                PointsForCorrectPick: options.PointsForCorrectPick,
                Tolerance: options.Tolerance,
                LowerTolerance: options.LowerTolerance,
                UpperTolerance: options.UpperTolerance,
                FormulaMaxPoints: options.FormulaMaxPoints,
                FormulaPenaltyPerUnit: options.FormulaPenaltyPerUnit));
        }

        return result;
    }

    private static bool IsRaceBonusPrompt(string rawPayload)
    {
        var commaIndex = rawPayload.IndexOf(',');
        var prompt = commaIndex >= 0
            ? rawPayload[..commaIndex].Trim()
            : rawPayload.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        return prompt.Contains("bonus", StringComparison.OrdinalIgnoreCase) ||
               prompt.Contains("gap", StringComparison.OrdinalIgnoreCase);
    }

    private static RaceBonusQuestionTemplateOptions ParseRaceBonusOptions(string? optionsJson, bool isDaveProfile)
    {
        if (!string.IsNullOrWhiteSpace(optionsJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<RaceBonusQuestionTemplateOptions>(optionsJson);
                if (parsed is not null)
                {
                    if (string.IsNullOrWhiteSpace(parsed.Mode))
                    {
                        parsed.Mode = "Exact";
                    }

                    if (parsed.PointsForCorrectPick <= 0)
                    {
                        parsed.PointsForCorrectPick = isDaveProfile ? 20 : 20;
                    }

                    return parsed;
                }
            }
            catch (JsonException)
            {
            }
        }

        return new RaceBonusQuestionTemplateOptions
        {
            Mode = "Exact",
            PointsForCorrectPick = isDaveProfile ? 20 : 20
        };
    }

    private static bool IsDaveSourcePath(string sourceFilePath)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath))
        {
            return false;
        }

        return !sourceFilePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<AdminMigrationParticipantComponentDeltaDto> BuildParticipantComponentDeltas(
        IReadOnlyList<AdminMigrationPickDiffDto> pickDiffs,
        IReadOnlyList<AdminMigrationPreseasonQuestionDiffDto> preseasonQuestionDiffs)
    {
        var raceBySubject = pickDiffs
            .GroupBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Imported = group.Sum(x => x.ImportedPoints ?? 0),
                    Calculated = group.Sum(x => x.CalculatedPoints ?? 0)
                },
                StringComparer.OrdinalIgnoreCase);

        var preseasonBySubject = preseasonQuestionDiffs
            .GroupBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new
                {
                    Imported = group.Sum(x => x.ImportedPoints ?? 0),
                    Calculated = group.Sum(x => x.CalculatedPoints ?? 0)
                },
                StringComparer.OrdinalIgnoreCase);

        var reasonsBySubject = pickDiffs
            .Where(x => x.DeltaPoints != 0)
            .Select(x => new { x.Subject, x.ReasonCode })
            .Concat(preseasonQuestionDiffs
                .Where(x => x.DeltaPoints != 0)
                .Select(x => new { x.Subject, x.ReasonCode }))
            .GroupBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(x => x.ReasonCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(reasonGroup => reasonGroup.Count())
                    .ThenBy(reasonGroup => reasonGroup.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(reasonGroup => (ReasonCode: (string?)reasonGroup.Key, Count: reasonGroup.Count()))
                    .FirstOrDefault(),
                StringComparer.OrdinalIgnoreCase);

        var subjects = raceBySubject.Keys
            .Concat(preseasonBySubject.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return subjects.Select(subject =>
        {
            var race = raceBySubject.GetValueOrDefault(subject);
            var preseason = preseasonBySubject.GetValueOrDefault(subject);
            var importedRace = race?.Imported ?? 0;
            var calculatedRace = race?.Calculated ?? 0;
            var importedPreseason = preseason?.Imported ?? 0;
            var calculatedPreseason = preseason?.Calculated ?? 0;
            var importedTotal = importedRace + importedPreseason;
            var calculatedTotal = calculatedRace + calculatedPreseason;
            var topReason = reasonsBySubject.GetValueOrDefault(subject);

            return new AdminMigrationParticipantComponentDeltaDto(
                Subject: subject,
                ImportedRacePoints: importedRace,
                CalculatedRacePoints: calculatedRace,
                ImportedPreseasonPoints: importedPreseason,
                CalculatedPreseasonPoints: calculatedPreseason,
                ImportedTotalPoints: importedTotal,
                CalculatedTotalPoints: calculatedTotal,
                NetDeltaPoints: calculatedTotal - importedTotal,
                TopReasonCode: topReason.ReasonCode,
                TopReasonCount: topReason.Count);
        }).ToArray();
    }

    private async Task<IReadOnlyList<AdminMigrationCdpParityDto>> BuildCdpParityAsync(Guid runId, CancellationToken cancellationToken)
    {
        var importedCdp = await _dbContext.MigrationImportLegacyPickScores
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId && x.PickType == "CDP")
            .GroupBy(x => x.Subject)
            .Select(group => new
            {
                Subject = group.Key,
                ImportedCdp = group.Any(x => x.LegacyPoints.HasValue)
                    ? group.Sum(x => x.LegacyPoints ?? 0)
                    : (int?)null
            })
            .ToDictionaryAsync(x => x.Subject, x => x.ImportedCdp, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var calculatedCdp = await _dbContext.MigrationImportCalculatedScores
            .AsNoTracking()
            .Where(x =>
                x.ImportRunId == runId &&
                (x.PickType == "1" || x.PickType == "2" || x.PickType == "3") &&
                EF.Functions.Like(x.ReasonCode, "PODIUM_EXACT%"))
            .GroupBy(x => x.Subject)
            .Select(group => new
            {
                Subject = group.Key,
                CalculatedCdp = group.Count()
            })
            .ToDictionaryAsync(x => x.Subject, x => x.CalculatedCdp, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var subjects = importedCdp.Keys
            .Concat(calculatedCdp.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return subjects
            .Select(subject =>
            {
                importedCdp.TryGetValue(subject, out var imported);
                calculatedCdp.TryGetValue(subject, out var calculated);
                var delta = calculated - (imported ?? 0);
                return new AdminMigrationCdpParityDto(
                    Subject: subject,
                    ImportedCdp: imported,
                    CalculatedCdp: calculated,
                    Delta: delta,
                    IsParity: imported.HasValue && delta == 0);
            })
            .ToArray();
    }

    private async Task<IReadOnlyList<AdminMigrationSourceManifestItemDto>> BuildSourceManifestAsync(Guid runId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.MigrationImportRawRows
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId)
            .Select(x => new { x.SourceFileName, x.SectionType })
            .ToArrayAsync(cancellationToken);

        return rows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.SourceFileName) ? "(unknown)" : x.SourceFileName!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new AdminMigrationSourceManifestItemDto(
                SourceFileName: group.Key,
                RowCount: group.Count(),
                HeaderCount: group.Count(x => x.SectionType == "Header"),
                RacePickCount: group.Count(x => x.SectionType == "RacePick"),
                SeasonQuestionPredictionCount: group.Count(x => x.SectionType == "SeasonQuestionPrediction"),
                RacePointsCount: group.Count(x => x.SectionType == "RacePoints"),
                TotalsMetaCount: group.Count(x => x.SectionType == "TotalsMeta"),
                UnclassifiedCount: group.Count(x => x.SectionType == "Unclassified"),
                SourceArtifactCount: group.Count(x => x.SectionType == "SourceArtifact")))
            .ToArray();
    }

    private static IReadOnlyList<AdminMigrationSourceContractDiagnosticDto> BuildSourceContractDiagnostics(
        string sourceFilePath,
        IReadOnlyList<AdminMigrationSourceManifestItemDto> sourceManifest)
    {
        var diagnostics = new List<AdminMigrationSourceContractDiagnosticDto>();
        var isDaveProfile = IsDaveSourcePath(sourceFilePath);

        if (!isDaveProfile)
        {
            diagnostics.Add(new AdminMigrationSourceContractDiagnosticDto(
                Code: "SOURCE_PROFILE",
                Severity: "Info",
                Message: "Phil CSV source profile detected."));
            return diagnostics;
        }

        diagnostics.Add(new AdminMigrationSourceContractDiagnosticDto(
            Code: "SOURCE_PROFILE",
            Severity: "Info",
            Message: "Dave multi-file source profile detected."));

        var expectedFiles = new[] { "races.csv", "bonus.csv", "bonusAnswers.csv", "Leaderboard.csv" };
        var manifestFiles = sourceManifest
            .Select(x => Path.GetFileName(x.SourceFileName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingFiles = expectedFiles
            .Where(file => !manifestFiles.Contains(file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (missingFiles.Length > 0)
        {
            diagnostics.Add(new AdminMigrationSourceContractDiagnosticDto(
                Code: "DAVE_CONTRACT_MISSING_FILES",
                Severity: "Warning",
                Message: $"Missing expected files in staged manifest: {string.Join(", ", missingFiles)}."));
        }
        else
        {
            diagnostics.Add(new AdminMigrationSourceContractDiagnosticDto(
                Code: "DAVE_CONTRACT_FILES_PRESENT",
                Severity: "Info",
                Message: "All required Dave package files are present in staged manifest."));
        }

        var unclassifiedRows = sourceManifest.Sum(x => x.UnclassifiedCount);
        if (unclassifiedRows > 0)
        {
            diagnostics.Add(new AdminMigrationSourceContractDiagnosticDto(
                Code: "UNCLASSIFIED_ROWS",
                Severity: "Warning",
                Message: $"Manifest contains {unclassifiedRows} unclassified rows across source files."));
        }

        return diagnostics;
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
        csv.AppendLine("category,questionId,questionText,participant,importedPoints,calculatedPoints,deltaPoints");
        foreach (var row in rows)
        {
            csv.Append(EscapeCsv(row.Category)).Append(',')
                .Append(EscapeCsv(row.QuestionId)).Append(',')
                .Append(EscapeCsv(row.QuestionText)).Append(',')
                .Append(EscapeCsv(row.Participant)).Append(',')
                .Append(row.ImportedPoints?.ToString(CultureInfo.InvariantCulture) ?? string.Empty).Append(',')
                .Append(row.CalculatedPoints.ToString(CultureInfo.InvariantCulture)).Append(',')
            .Append(row.DeltaPoints.ToString(CultureInfo.InvariantCulture))
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
        var season = await _dbContext.MigrationImportRaceRoundMappings
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId && x.Season.HasValue)
            .Select(x => x.Season!.Value)
            .FirstOrDefaultAsync(cancellationToken);

        var templateQuery = _dbContext.QuestionTemplates.AsNoTracking();
        if (season != 0)
        {
            templateQuery = templateQuery.Where(t => t.Season == season);
        }

        var rows = await _dbContext.QuestionScores
            .AsNoTracking()
            .Join(
                templateQuery,
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
                    score.DeltaPoints
                })
            .OrderBy(x => x.Category)
            .ThenBy(x => x.QuestionId)
            .ThenBy(x => x.ParticipantId)
            .ToArrayAsync(cancellationToken);

        return rows
            .Select(x => new AdminMigrationQuestionDiffDto(
                x.Category.ToString(),
                x.QuestionId,
                x.Prompt,
                x.ParticipantId,
                x.ImportedPoints,
                x.CalculatedPoints,
                x.DeltaPoints))
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

        if (IsPathWithinRoot(candidatePath, importRoot) ||
            IsPathWithinRoot(candidatePath, tempImportRoot))
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