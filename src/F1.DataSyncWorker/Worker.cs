using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IDataSyncOrchestrator _orchestrator;
    private readonly IMigrationImportOrchestrator _migrationImportOrchestrator;
    private readonly DataSyncOptions _dataSyncOptions;
    private readonly MigrationImportOptions _migrationImportOptions;

    public Worker(
        ILogger<Worker> logger,
        IDataSyncOrchestrator orchestrator,
        IMigrationImportOrchestrator migrationImportOrchestrator,
        IOptions<DataSyncOptions> dataSyncOptions,
        IOptions<MigrationImportOptions> migrationImportOptions)
    {
        _logger = logger;
        _orchestrator = orchestrator;
        _migrationImportOrchestrator = migrationImportOrchestrator;
        _dataSyncOptions = dataSyncOptions.Value;
        _migrationImportOptions = migrationImportOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("F1 data sync worker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_migrationImportOptions.Enabled)
                {
                    await _migrationImportOrchestrator.RunOnceAsync(stoppingToken);
                }
                else
                {
                    await _orchestrator.RunOnceAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker run failed.");

                // Migration import mode is a one-shot operation and must fail fast
                // so CI/ops can detect failures from the process exit code.
                if (_migrationImportOptions.Enabled || !_dataSyncOptions.ContinueOnError)
                {
                    throw;
                }
            }

            if (_migrationImportOptions.Enabled)
            {
                _logger.LogInformation("Migration import mode runs once per process. Exiting.");
                break;
            }

            if (_dataSyncOptions.IntervalMinutes <= 0)
            {
                _logger.LogInformation("IntervalMinutes is {IntervalMinutes}. Exiting after single run.", _dataSyncOptions.IntervalMinutes);
                break;
            }

            var delay = TimeSpan.FromMinutes(_dataSyncOptions.IntervalMinutes);
            _logger.LogInformation("Next data sync run scheduled in {DelayMinutes} minutes.", _dataSyncOptions.IntervalMinutes);
            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogInformation("F1 data sync worker stopped.");
    }
}
