using System.Text.RegularExpressions;
using F1.Core.Models;
using F1.DataSyncWorker.Options;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationCanonicalWriteService : IMigrationCanonicalWriteService
{
    private const string ActualSubject = "ACTUAL";
    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly MigrationImportOptions _importOptions;

    public MigrationCanonicalWriteService(
        IDbContextFactory<F1DbContext> dbContextFactory,
        IOptions<MigrationImportOptions> importOptions)
    {
        _dbContextFactory = dbContextFactory;
        _importOptions = importOptions.Value;
    }

    public async Task PersistCanonicalEntitiesAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var run = await dbContext.MigrationImportRuns
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Migration import run {runId} not found.");

        if (run.IsDryRun)
        {
            return;
        }

        var selections = await dbContext.MigrationImportRaceSelections
            .Where(x => x.ImportRunId == runId && !x.IsActualOutcome && x.Subject != ActualSubject)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        if (selections.Count == 0)
        {
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var competition = await dbContext.Competitions
                .OrderBy(x => x.Id)
                .FirstOrDefaultAsync(x => x.Year == _importOptions.Season, cancellationToken);

            if (competition is null)
            {
                competition = new Competition
                {
                    Name = $"Migration Import {_importOptions.Season}",
                    Year = _importOptions.Season,
                    Description = "Auto-created by migration canonical writer"
                };

                dbContext.Competitions.Add(competition);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var raceCodes = selections
                .Select(x => x.RaceCode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingRaces = await dbContext.Races
                .Where(x => x.CompetitionId == competition.Id && x.Season == _importOptions.Season)
                .ToListAsync(cancellationToken);

            var existingRaceByCircuit = existingRaces
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var raceIdByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var round = 1;
            foreach (var raceCode in raceCodes)
            {
                var canonicalRaceId = BuildRaceId(_importOptions.Season, raceCode);
                raceIdByCode[raceCode] = canonicalRaceId;

                if (existingRaceByCircuit.ContainsKey(canonicalRaceId))
                {
                    round++;
                    continue;
                }

                dbContext.Races.Add(new Race
                {
                    Id = canonicalRaceId,
                    CompetitionId = competition.Id,
                    Season = _importOptions.Season,
                    Round = round,
                    RaceName = raceCode,
                    CircuitName = raceCode,
                    StartTimeUtc = DateTime.UtcNow,
                    PreQualyDeadlineUtc = DateTime.UtcNow,
                    FinalDeadlineUtc = DateTime.UtcNow
                });
                round++;
            }

            var driverIds = selections
                .SelectMany(x => ExtractDriverIds(x.NormalizedValue))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingDriverIds = await dbContext.Drivers
                .Select(x => x.DriverId ?? string.Empty)
                .ToListAsync(cancellationToken);
            var existingDriverIdSet = existingDriverIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var driverId in driverIds)
            {
                if (existingDriverIdSet.Contains(driverId))
                {
                    continue;
                }

                dbContext.Drivers.Add(new Driver
                {
                    DriverId = driverId,
                    FullName = driverId,
                    Code = driverId
                });
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (string.Equals(_importOptions.CanonicalWriteFailureInjectionStage, "after_drivers", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Injected canonical write failure after driver/race stage.");
            }

            var groupedSelections = selections
                .GroupBy(x => new { x.Subject, x.RaceCode })
                .ToList();

            foreach (var group in groupedSelections)
            {
                if (!raceIdByCode.TryGetValue(group.Key.RaceCode, out var raceId))
                {
                    continue;
                }

                var existingSelection = await dbContext.Selections
                    .FirstOrDefaultAsync(
                        x => x.RaceId == raceId && x.UserId == group.Key.Subject,
                        cancellationToken);

                if (existingSelection is null)
                {
                    existingSelection = new Selection
                    {
                        Id = Guid.NewGuid(),
                        UserId = group.Key.Subject,
                        RaceId = raceId,
                        BetType = BetType.Regular,
                        SubmittedAtUtc = DateTime.UtcNow
                    };
                    dbContext.Selections.Add(existingSelection);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await dbContext.SelectionPositions
                    .Where(x => x.SelectionId == existingSelection.Id)
                    .ExecuteDeleteAsync(cancellationToken);

                var ordered = group
                    .Where(x => int.TryParse(x.PickType, out _))
                    .Select(x => new
                    {
                        Pick = int.Parse(x.PickType),
                        Driver = ExtractDriverIds(x.NormalizedValue).FirstOrDefault()
                    })
                    .Where(x => x.Pick > 0 && x.Driver is not null)
                    .OrderBy(x => x.Pick)
                    .ToList();

                foreach (var item in ordered)
                {
                    dbContext.SelectionPositions.Add(new SelectionPositionEntity
                    {
                        SelectionId = existingSelection.Id,
                        Position = item.Pick,
                        DriverId = item.Driver!
                    });
                }

                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string BuildRaceId(int season, string raceCode)
    {
        var trimmedCode = raceCode.Trim();
        var normalized = NonAlphaNumericRegex().Replace(trimmedCode.ToLowerInvariant(), "-").Trim('-');
        var id = $"migration-{season}-{normalized}";
        return id.Length <= 128 ? id : id[..128];
    }

    private static IEnumerable<string> ExtractDriverIds(string? normalizedValue)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return [];
        }

        return DriverTokenSplitRegex().Split(normalizedValue)
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x.Length > 0 && x.Length <= 64)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("[\\s,;/|]+", RegexOptions.Compiled)]
    private static partial Regex DriverTokenSplitRegex();
}
