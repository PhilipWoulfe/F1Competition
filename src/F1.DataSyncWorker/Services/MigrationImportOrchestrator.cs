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
    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly DataSyncOptions _dataSyncOptions;
    private readonly MigrationImportOptions _importOptions;
    private int _migrationsApplied;

    public MigrationImportOrchestrator(
        ILogger<MigrationImportOrchestrator> logger,
        IMigrationImportRunService runService,
        IMigrationImportRowClassifier rowClassifier,
        IMigrationRaceSelectionParser raceSelectionParser,
        IDbContextFactory<F1DbContext> dbContextFactory,
        IOptions<DataSyncOptions> dataSyncOptions,
        IOptions<MigrationImportOptions> importOptions)
    {
        _logger = logger;
        _runService = runService;
        _rowClassifier = rowClassifier;
        _raceSelectionParser = raceSelectionParser;
        _dbContextFactory = dbContextFactory;
        _dataSyncOptions = dataSyncOptions.Value;
        _importOptions = importOptions.Value;
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
        _logger.LogInformation(
            "Migration import run started. RunId={RunId}, DryRun={DryRun}, Source={SourceFilePath}",
            run.RunId,
            run.IsDryRun,
            run.SourceFilePath);

        try
        {
            var totalRows = await StageRawRowsAsync(run.RunId, sourceFilePath, cancellationToken);
            var parsedRaceSelections = await _raceSelectionParser.ParseAndPersistAsync(run.RunId, cancellationToken);
            await _runService.CompleteRunAsync(run.RunId, totalRows, cancellationToken);

            _logger.LogInformation(
                "Migration import run completed. RunId={RunId}, RowsStaged={RowsStaged}, RaceSelectionsParsed={RaceSelectionsParsed}, Checksum={Checksum}",
                run.RunId,
                totalRows,
                parsedRaceSelections,
                run.SourceFileChecksum);
        }
        catch (Exception ex)
        {
            await _runService.FailRunAsync(run.RunId, ex.Message, cancellationToken);
            _logger.LogError(ex, "Migration import run failed. RunId={RunId}", run.RunId);
            throw;
        }
    }

    private async Task<int> StageRawRowsAsync(Guid runId, string sourceFilePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(sourceFilePath);
        using var reader = new StreamReader(stream);

        var rowNumber = 0;
        var stagedCount = 0;
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
            batch.Add(_rowClassifier.Classify(rowNumber, line));

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

        return stagedCount;
    }

    private static string ResolveSourceFilePath(string sourceFilePath)
    {
        if (Path.IsPathRooted(sourceFilePath))
        {
            return sourceFilePath;
        }

        return Path.GetFullPath(sourceFilePath, Directory.GetCurrentDirectory());
    }
}