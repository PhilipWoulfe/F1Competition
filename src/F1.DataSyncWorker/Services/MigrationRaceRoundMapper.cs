using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Options;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.Services;

public sealed class MigrationRaceRoundMapper : IMigrationRaceRoundMapper
{
    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly IJolpicaClient _jolpicaClient;
    private readonly DataSyncOptions _dataSyncOptions;
    private readonly MigrationImportOptions _importOptions;

    public MigrationRaceRoundMapper(
        IDbContextFactory<F1DbContext> dbContextFactory,
        IJolpicaClient jolpicaClient,
        IOptions<DataSyncOptions> dataSyncOptions,
        IOptions<MigrationImportOptions> importOptions)
    {
        _dbContextFactory = dbContextFactory;
        _jolpicaClient = jolpicaClient;
        _dataSyncOptions = dataSyncOptions.Value;
        _importOptions = importOptions.Value;
    }

    public async Task<(int SnapshotCount, int MappingCount, int WarningCount)> MapAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        var jolpicaRaces = await _jolpicaClient.GetRacesAsync(
            _importOptions.Season,
            _dataSyncOptions.HttpRetryCount,
            _dataSyncOptions.HttpRetryDelayMs,
            cancellationToken);

        var orderedRaces = jolpicaRaces
            .Select(race => new
            {
                Race = race,
                Round = int.TryParse(race.Round, out var round) ? round : int.MaxValue,
                Season = int.TryParse(race.Season, out var season) ? season : _importOptions.Season
            })
            .Where(x => x.Round != int.MaxValue)
            .OrderBy(x => x.Round)
            .ToList();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var startRows = await dbContext.MigrationImportRaceSelections
            .Where(x => x.ImportRunId == runId && x.PickType == "1" && !x.IsActualOutcome)
            .Select(x => new { x.RowNumber, x.RaceCode })
            .Distinct()
            .OrderBy(x => x.RowNumber)
            .ToListAsync(cancellationToken);

        dbContext.MigrationImportJolpicaRaceSnapshots.RemoveRange(
            dbContext.MigrationImportJolpicaRaceSnapshots.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportRaceRoundMappings.RemoveRange(
            dbContext.MigrationImportRaceRoundMappings.Where(x => x.ImportRunId == runId));
        await dbContext.SaveChangesAsync(cancellationToken);

        var snapshots = orderedRaces.Select(item => new MigrationImportJolpicaRaceSnapshotEntity
        {
            ImportRunId = runId,
            Season = item.Season,
            Round = item.Round,
            RaceName = item.Race.RaceName,
            CircuitName = item.Race.Circuit?.CircuitName,
            StartTimeUtc = TryParseStartTimeUtc(item.Race.Date, item.Race.Time)
        }).ToList();

        var mappings = new List<MigrationImportRaceRoundMappingEntity>();
        var seenRaceCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < startRows.Count; index++)
        {
            var start = startRows[index];
            var mappedRace = index < orderedRaces.Count ? orderedRaces[index] : null;
            string? warning = null;

            if (seenRaceCodes.TryGetValue(start.RaceCode, out var firstSequence))
            {
                warning = $"Source race code {start.RaceCode} appears in multiple positions (first sequence {firstSequence}); sequence-based mapping applied.";
            }
            else
            {
                seenRaceCodes[start.RaceCode] = index + 1;
            }

            if (mappedRace is null)
            {
                warning = AppendWarning(warning, "No Jolpica race available for this sequence position.");
            }

            mappings.Add(new MigrationImportRaceRoundMappingEntity
            {
                ImportRunId = runId,
                RaceSequence = index + 1,
                SourceRowNumber = start.RowNumber,
                SourceRaceCode = start.RaceCode,
                Season = mappedRace?.Season,
                Round = mappedRace?.Round,
                MappedRaceName = mappedRace?.Race.RaceName,
                Warning = warning
            });
        }

        await dbContext.MigrationImportJolpicaRaceSnapshots.AddRangeAsync(snapshots, cancellationToken);
        await dbContext.MigrationImportRaceRoundMappings.AddRangeAsync(mappings, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var warningCount = mappings.Count(x => !string.IsNullOrWhiteSpace(x.Warning));
        return (snapshots.Count, mappings.Count, warningCount);
    }

    private static DateTime? TryParseStartTimeUtc(string date, string? time)
    {
        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
        {
            return null;
        }

        if (!DateTime.TryParse($"{date}T{time}", null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return null;
        }

        return parsed;
    }

    private static string AppendWarning(string? existing, string message)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return message;
        }

        return $"{existing} {message}";
    }
}