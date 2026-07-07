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
            var normalizedConflictPolicy = NormalizeConflictPolicy(_importOptions.CanonicalConflictPolicy);
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

            var existingRaceById = existingRaces
                .GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            var existingRaceByRound = existingRaces
                .GroupBy(x => x.Round)
                .ToDictionary(x => x.Key, x => x.First());

            var mappedRoundByRaceCode = await dbContext.MigrationImportRaceRoundMappings
                .AsNoTracking()
                .Where(x =>
                    x.ImportRunId == runId &&
                    x.Round.HasValue &&
                    !string.IsNullOrWhiteSpace(x.MappedCircuitId))
                .GroupBy(x => x.MappedCircuitId!)
                .Select(group => new
                {
                    RaceCode = group.Key,
                    Round = group.Min(item => item.Round!.Value)
                })
                .ToDictionaryAsync(x => x.RaceCode, x => x.Round, StringComparer.OrdinalIgnoreCase, cancellationToken);

            var raceIdByCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var occupiedRounds = existingRaces
                .Select(x => x.Round)
                .ToHashSet();
            var nextRoundCursor = 1;
            foreach (var raceCode in raceCodes)
            {
                var canonicalRaceId = BuildRaceId(_importOptions.Season, raceCode);

                if (existingRaceById.TryGetValue(canonicalRaceId, out var existingRaceByCanonicalId))
                {
                    raceIdByCode[raceCode] = existingRaceByCanonicalId.Id;
                    continue;
                }

                var desiredRound = mappedRoundByRaceCode.TryGetValue(raceCode, out var mappedRound)
                    ? mappedRound
                    : nextRoundCursor;

                if (existingRaceByRound.TryGetValue(desiredRound, out var existingRaceByDesiredRound))
                {
                    raceIdByCode[raceCode] = existingRaceByDesiredRound.Id;
                    continue;
                }

                while (occupiedRounds.Contains(desiredRound))
                {
                    desiredRound++;
                }

                raceIdByCode[raceCode] = canonicalRaceId;

                var createdRace = new Race
                {
                    Id = canonicalRaceId,
                    CompetitionId = competition.Id,
                    Season = _importOptions.Season,
                    Round = desiredRound,
                    RaceName = raceCode,
                    CircuitName = raceCode,
                    StartTimeUtc = DateTime.UtcNow,
                    PreQualyDeadlineUtc = DateTime.UtcNow,
                    FinalDeadlineUtc = DateTime.UtcNow
                };

                dbContext.Races.Add(createdRace);
                existingRaceById[canonicalRaceId] = createdRace;
                existingRaceByRound[desiredRound] = createdRace;
                occupiedRounds.Add(desiredRound);
                nextRoundCursor = Math.Max(nextRoundCursor, desiredRound + 1);
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

            var incomingScopes = groupedSelections
                .Select(group => new IncomingSelectionScope(
                    group.Key.Subject,
                    group.Key.RaceCode,
                    raceIdByCode.TryGetValue(group.Key.RaceCode, out var raceId) ? raceId : string.Empty,
                    group.Min(x => x.RowNumber)))
                .Where(x => !string.IsNullOrWhiteSpace(x.RaceId))
                .ToList();

            var incomingRaceIds = incomingScopes.Select(x => x.RaceId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var incomingSubjects = incomingScopes.Select(x => x.Subject).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            var existingSelections = await dbContext.Selections
                .Where(x => incomingRaceIds.Contains(x.RaceId) && incomingSubjects.Contains(x.UserId))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var existingSelectionKeys = existingSelections
                .Select(x => BuildSelectionKey(x.RaceId, x.UserId))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var conflictDiagnostics = incomingScopes
                .Where(scope => existingSelectionKeys.Contains(BuildSelectionKey(scope.RaceId, scope.Subject)))
                .Select(scope => new MigrationImportConflictDiagnosticEntity
                {
                    ImportRunId = runId,
                    EntityType = "Selection",
                    ConflictType = "existing_active_selection",
                    KeyFields = BuildSelectionKey(scope.RaceId, scope.Subject),
                    SourceReference = $"row:{scope.SourceRowNumber}|race:{scope.RaceCode}|subject:{scope.Subject}",
                    PolicyOutcome = ResolvePolicyOutcome(normalizedConflictPolicy),
                    RecommendedAction = ResolveRecommendedAction(normalizedConflictPolicy),
                    CreatedAtUtc = DateTime.UtcNow
                })
                .ToList();

            if (conflictDiagnostics.Count > 0)
            {
                if (normalizedConflictPolicy == "fail")
                {
                    await PersistConflictDiagnosticsAsync(conflictDiagnostics, cancellationToken);
                    throw new InvalidOperationException(
                        $"Canonical write conflict policy blocked commit. ConflictCount={conflictDiagnostics.Count}, Policy={normalizedConflictPolicy}.");
                }

                await dbContext.MigrationImportConflictDiagnostics.AddRangeAsync(conflictDiagnostics, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var skippedSelectionKeys = conflictDiagnostics
                .Where(x => string.Equals(x.PolicyOutcome, "Skipped", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.KeyFields)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groupedSelections)
            {
                if (!raceIdByCode.TryGetValue(group.Key.RaceCode, out var raceId))
                {
                    continue;
                }

                var selectionKey = BuildSelectionKey(raceId, group.Key.Subject);
                if (skippedSelectionKeys.Contains(selectionKey))
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

    private static string NormalizeConflictPolicy(string? policy)
    {
        if (string.IsNullOrWhiteSpace(policy))
        {
            return "override";
        }

        var normalized = policy.Trim().ToLowerInvariant();
        return normalized is "fail" or "skip" or "override" ? normalized : "override";
    }

    private static string ResolvePolicyOutcome(string policy)
    {
        return policy switch
        {
            "fail" => "Failed",
            "skip" => "Skipped",
            _ => "Overridden"
        };
    }

    private static string ResolveRecommendedAction(string policy)
    {
        return policy switch
        {
            "fail" => "Review conflicting canonical rows and rerun with approved policy.",
            "skip" => "Review skipped entities and run targeted reconciliation.",
            _ => "Verify overridden rows in reconciliation report."
        };
    }

    private static string BuildSelectionKey(string raceId, string subject)
    {
        return $"raceId:{raceId}|subject:{subject}";
    }

    private async Task PersistConflictDiagnosticsAsync(
        IReadOnlyCollection<MigrationImportConflictDiagnosticEntity> diagnostics,
        CancellationToken cancellationToken)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        await using var context = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var detachedDiagnostics = diagnostics.Select(item => new MigrationImportConflictDiagnosticEntity
        {
            ImportRunId = item.ImportRunId,
            EntityType = item.EntityType,
            ConflictType = item.ConflictType,
            KeyFields = item.KeyFields,
            SourceReference = item.SourceReference,
            PolicyOutcome = item.PolicyOutcome,
            RecommendedAction = item.RecommendedAction,
            CreatedAtUtc = item.CreatedAtUtc
        });

        await context.MigrationImportConflictDiagnostics.AddRangeAsync(detachedDiagnostics, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
    }

    private readonly record struct IncomingSelectionScope(
        string Subject,
        string RaceCode,
        string RaceId,
        int SourceRowNumber);
}
