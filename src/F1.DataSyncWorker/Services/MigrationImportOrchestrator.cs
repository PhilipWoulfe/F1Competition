using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Options;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.Services;

public sealed class MigrationImportOrchestrator : IMigrationImportOrchestrator
{
    private const int BatchSize = 500;
    private readonly ILogger<MigrationImportOrchestrator> _logger;
    private readonly IMigrationImportRunService _runService;
    private readonly IMigrationImportRowClassifier _rowClassifier;
    private readonly IMigrationRaceSelectionParser _raceSelectionParser;
    private readonly IMigrationRaceRoundMapper _raceRoundMapper;
    private readonly IMigrationScoreRecalculator _scoreRecalculator;
    private readonly IMigrationLegacyScoreImporter _legacyScoreImporter;
    private readonly IMigrationReconciliationService _reconciliationService;
    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly DataSyncOptions _dataSyncOptions;
    private readonly MigrationImportOptions _importOptions;
    private readonly IMigrationExpectedVarianceRuleSetMetadataProvider _ruleSetMetadataProvider;
    private int _migrationsApplied;

    private readonly record struct RawRowStageResult(int StagedRowCount, int RejectedRowCount);

    public MigrationImportOrchestrator(
        ILogger<MigrationImportOrchestrator> logger,
        IMigrationImportRunService runService,
        IMigrationImportRowClassifier rowClassifier,
        IMigrationRaceSelectionParser raceSelectionParser,
        IMigrationRaceRoundMapper raceRoundMapper,
        IMigrationScoreRecalculator scoreRecalculator,
        IMigrationLegacyScoreImporter legacyScoreImporter,
        IMigrationReconciliationService reconciliationService,
        IDbContextFactory<F1DbContext> dbContextFactory,
        IOptions<DataSyncOptions> dataSyncOptions,
        IOptions<MigrationImportOptions> importOptions,
        IMigrationExpectedVarianceRuleSetMetadataProvider ruleSetMetadataProvider)
    {
        _logger = logger;
        _runService = runService;
        _rowClassifier = rowClassifier;
        _raceSelectionParser = raceSelectionParser;
        _raceRoundMapper = raceRoundMapper;
        _scoreRecalculator = scoreRecalculator;
        _legacyScoreImporter = legacyScoreImporter;
        _reconciliationService = reconciliationService;
        _dbContextFactory = dbContextFactory;
        _dataSyncOptions = dataSyncOptions.Value;
        _importOptions = importOptions.Value;
        _ruleSetMetadataProvider = ruleSetMetadataProvider;
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_dataSyncOptions.AutoMigrate && Interlocked.CompareExchange(ref _migrationsApplied, 1, 0) == 0)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            _logger.LogInformation("Applying EF migrations before migration import run.");
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var sourceFilePath = ResolveSourceFilePath(_importOptions.SourceFilePath);
        var run = await _runService.StartRunAsync(sourceFilePath, _importOptions.DryRun, cancellationToken);
        await ExecuteRunAsync(run, cancellationToken);
    }

    public async Task<bool> RunNextQueuedAsync(CancellationToken cancellationToken)
    {
        if (_dataSyncOptions.AutoMigrate && Interlocked.CompareExchange(ref _migrationsApplied, 1, 0) == 0)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            _logger.LogInformation("Applying EF migrations before queued migration import run.");
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var run = await _runService.TryClaimNextQueuedRunAsync(cancellationToken);
        if (run is null)
        {
            return false;
        }

        await ExecuteRunAsync(run, cancellationToken);
        return true;
    }

    private async Task ExecuteRunAsync(MigrationImportRunContext run, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Migration import run started. RunId={RunId}, DryRun={DryRun}, Source={SourceFilePath}",
            run.RunId,
            run.IsDryRun,
            run.SourceFilePath);

        _logger.LogInformation(
            "Migration expected variance ruleset applied. RunId={RunId}, Environment={Environment}, RuleSetId={RuleSetId}, RuleSetVersion={RuleSetVersion}, RuleSetChecksum={RuleSetChecksum}, ActiveRuleCount={ActiveRuleCount}, RuleSource={RuleSource}, RulesEnabled={RulesEnabled}",
            run.RunId,
            _ruleSetMetadataProvider.ActiveEnvironment,
            _ruleSetMetadataProvider.RuleSetId,
            _ruleSetMetadataProvider.RuleSetVersion,
            _ruleSetMetadataProvider.RuleSetChecksum,
            _ruleSetMetadataProvider.ActiveRuleCount,
            _ruleSetMetadataProvider.RuleSource,
            _ruleSetMetadataProvider.IsEnabled);

        try
        {
            var rawRows = await StageRawRowsAsync(run.RunId, run.SourceFilePath, cancellationToken);
            var parseResult = await _raceSelectionParser.ParseAndPersistAsync(run.RunId, cancellationToken);
            if (parseResult.UnresolvedTokenCount > 0)
            {
                if (_importOptions.UnresolvedTokenFailThreshold > 0 &&
                    parseResult.UnresolvedTokenCount >= _importOptions.UnresolvedTokenFailThreshold)
                {
                    throw new InvalidOperationException(
                        $"Migration import unresolved token threshold reached. UnresolvedTokenCount={parseResult.UnresolvedTokenCount}, Threshold={_importOptions.UnresolvedTokenFailThreshold}.");
                }

                _logger.LogWarning(
                    "Migration import completed with unresolved tokens below fail threshold. RunId={RunId}, UnresolvedTokenCount={UnresolvedTokenCount}, FailThreshold={FailThreshold}",
                    run.RunId,
                    parseResult.UnresolvedTokenCount,
                    _importOptions.UnresolvedTokenFailThreshold);
            }

            var mappingResult = (SnapshotCount: 0, MappingCount: 0, WarningCount: 0);
            var selectionRaceCodesRewritten = 0;
            if (!run.IsDryRun)
            {
                mappingResult = await _raceRoundMapper.MapAndPersistAsync(run.RunId, cancellationToken);
                selectionRaceCodesRewritten = await RewriteSelectionRaceCodesToMappedCircuitIdsAsync(run.RunId, cancellationToken);
            }
            else
            {
                _logger.LogInformation(
                    "Migration import run in dry-run mode; skipping race-round mapping and Jolpica fetch. RunId={RunId}",
                    run.RunId);
            }

            var scoreResult = await _scoreRecalculator.RecalculateAndPersistAsync(run.RunId, cancellationToken);
            var legacyResult = await _legacyScoreImporter.ImportAndPersistAsync(run.RunId, cancellationToken);
            var reconciliationResult = await _reconciliationService.ReconcileAndPersistAsync(run.RunId, cancellationToken);

            await _runService.CompleteRunAsync(run.RunId, rawRows.StagedRowCount, cancellationToken);

            _logger.LogInformation(
                "Migration import summary. RunId={RunId}, Season={Season}, DryRun={DryRun}, Source={SourceFilePath}, RowsParsed={RowsParsed}, RowsRejected={RowsRejected}, UnresolvedTokens={UnresolvedTokens}, TotalDelta={TotalDelta}",
                run.RunId,
                _importOptions.Season,
                run.IsDryRun,
                run.SourceFilePath,
                rawRows.StagedRowCount,
                rawRows.RejectedRowCount,
                parseResult.UnresolvedTokenCount,
                reconciliationResult.TotalDelta);

            _logger.LogInformation(
                "Migration import run completed. RunId={RunId}, RowsStaged={RowsStaged}, RaceSelectionsParsed={RaceSelectionsParsed}, JolpicaSnapshots={JolpicaSnapshots}, RoundMappings={RoundMappings}, MappingWarnings={MappingWarnings}, SelectionRaceCodesRewritten={SelectionRaceCodesRewritten}, ScoredPicks={ScoredPicks}, CalculatedPoints={CalculatedPoints}, LegacyPickScores={LegacyPickScores}, ImportedTotals={ImportedTotals}, CalculatedTotals={CalculatedTotals}, PickDiffs={PickDiffs}, RaceDiffs={RaceDiffs}, ParticipantDeltaSummaries={ParticipantDeltaSummaries}, ReasonSummaries={ReasonSummaries}, NetDelta={NetDelta}, Checksum={Checksum}",
                run.RunId,
                rawRows.StagedRowCount,
                parseResult.SelectionCount,
                mappingResult.SnapshotCount,
                mappingResult.MappingCount,
                mappingResult.WarningCount,
                selectionRaceCodesRewritten,
                scoreResult.ScoredPickCount,
                scoreResult.TotalPoints,
                legacyResult.LegacyPickScoreCount,
                legacyResult.ImportedTotalCount,
                legacyResult.CalculatedTotalCount,
                reconciliationResult.PickDiffCount,
                reconciliationResult.RaceDiffCount,
                reconciliationResult.ParticipantSummaryCount,
                reconciliationResult.ReasonSummaryCount,
                reconciliationResult.TotalDelta,
                run.SourceFileChecksum);
        }
        catch (Exception ex)
        {
            await _runService.FailRunAsync(run.RunId, ex.Message, cancellationToken);
            _logger.LogError(ex, "Migration import run failed. RunId={RunId}", run.RunId);
            throw;
        }
    }

    private async Task<RawRowStageResult> StageRawRowsAsync(Guid runId, string sourceFilePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(sourceFilePath);
        using var reader = new StreamReader(stream);
        var applyPhil2025ContractPolicy = MigrationPhil2025CsvContractPolicy.AppliesTo(sourceFilePath);

        var rowNumber = 0;
        var stagedCount = 0;
        var rejectedCount = 0;
        var batch = new List<StagedImportRow>(BatchSize);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                continue;
            }

            rowNumber++;
            var stagedRow = _rowClassifier.Classify(rowNumber, line);
            if (applyPhil2025ContractPolicy)
            {
                stagedRow = MigrationPhil2025CsvContractPolicy.Apply(stagedRow);
            }

            batch.Add(stagedRow);
            if (string.Equals(stagedRow.SectionType, MigrationImportSectionTypes.Unclassified, StringComparison.Ordinal))
            {
                rejectedCount++;
            }

            if (batch.Count < BatchSize)
            {
                continue;
            }

            await _runService.StageRowsAsync(runId, batch, cancellationToken);
            stagedCount += batch.Count;
            batch.Clear();
        }

        if (batch.Count > 0)
        {
            await _runService.StageRowsAsync(runId, batch, cancellationToken);
            stagedCount += batch.Count;
        }

        return new RawRowStageResult(stagedCount, rejectedCount);
    }

    private static string ResolveSourceFilePath(string sourceFilePath)
    {
        if (Path.IsPathRooted(sourceFilePath))
        {
            return sourceFilePath;
        }

        return Path.GetFullPath(sourceFilePath, Directory.GetCurrentDirectory());
    }

    private async Task<int> RewriteSelectionRaceCodesToMappedCircuitIdsAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var mappings = await dbContext.MigrationImportRaceRoundMappings
            .Where(x => x.ImportRunId == runId && !string.IsNullOrWhiteSpace(x.MappedCircuitId))
            .OrderBy(x => x.SourceRowNumber)
            .Select(x => new { x.SourceRowNumber, x.MappedCircuitId })
            .ToListAsync(cancellationToken);

        if (mappings.Count == 0)
        {
            return 0;
        }

        var rewritten = 0;
        for (var index = 0; index < mappings.Count; index++)
        {
            var startRow = mappings[index].SourceRowNumber;
            var endRow = index + 1 < mappings.Count
                ? mappings[index + 1].SourceRowNumber - 1
                : int.MaxValue;
            var mappedCircuitId = mappings[index].MappedCircuitId!;

            var updated = await dbContext.MigrationImportRaceSelections
                .Where(x =>
                    x.ImportRunId == runId &&
                    x.RowNumber >= startRow &&
                    x.RowNumber <= endRow &&
                    x.RaceCode != mappedCircuitId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(selection => selection.RaceCode, mappedCircuitId),
                    cancellationToken);

            rewritten += updated;
        }

        return rewritten;
    }
}