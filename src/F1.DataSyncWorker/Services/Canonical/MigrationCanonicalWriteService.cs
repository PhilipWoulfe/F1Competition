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
    private static readonly Dictionary<string, string> JolpicaDriverIdByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALB"] = "albon",
        ["ALO"] = "alonso",
        ["ANT"] = "antonelli",
        ["BEA"] = "bearman",
        ["BOR"] = "bortoleto",
        ["BOT"] = "bottas",
        ["COL"] = "colapinto",
        ["DOO"] = "doohan",
        ["GAS"] = "gasly",
        ["HAD"] = "hadjar",
        ["HAM"] = "hamilton",
        ["HUL"] = "hulkenberg",
        ["LAW"] = "lawson",
        ["LEC"] = "leclerc",
        ["LIN"] = "lindblad",
        ["MAG"] = "magnussen",
        ["NOR"] = "norris",
        ["OCO"] = "ocon",
        ["PER"] = "perez",
        ["PIA"] = "piastri",
        ["RIC"] = "ricciardo",
        ["RUS"] = "russell",
        ["SAI"] = "sainz",
        ["STR"] = "stroll",
        ["TSU"] = "tsunoda",
        ["VER"] = "max_verstappen",
        ["ZHO"] = "zhou"
    };
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
                .Where(x => x.Year == _importOptions.Season)
                .OrderBy(x => x.Name == "Philip 2025" ? 0 : 1)
                .ThenBy(x => x.Name.Contains("Philip") ? 0 : 1)
                .ThenBy(x => x.Name.StartsWith("Migration Import") ? 1 : 0)
                .ThenBy(x => x.Id)
                .FirstOrDefaultAsync(cancellationToken);

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

            var existingRaceByRound = existingRaces
                .GroupBy(x => x.Round)
                .ToDictionary(x => x.Key, x => x.First());

            var existingRaceByCircuitCode = existingRaces
                .SelectMany(race => new[]
                {
                    new { Key = RaceCodeNormalizer.NormalizeRaceCode(race.CircuitName), Race = race },
                    new { Key = RaceCodeNormalizer.NormalizeRaceCode(race.RaceName), Race = race }
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First().Race, StringComparer.OrdinalIgnoreCase);

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
            var unresolvedRaceCodes = new List<string>();

            foreach (var raceCode in raceCodes)
            {
                if (mappedRoundByRaceCode.TryGetValue(raceCode, out var mappedRound) &&
                    existingRaceByRound.TryGetValue(mappedRound, out var existingRaceByMappedRound))
                {
                    raceIdByCode[raceCode] = existingRaceByMappedRound.Id;
                    continue;
                }

                if (existingRaceByCircuitCode.TryGetValue(raceCode, out var existingRaceByCircuit))
                {
                    raceIdByCode[raceCode] = existingRaceByCircuit.Id;
                    continue;
                }

                unresolvedRaceCodes.Add(raceCode);
            }

            if (unresolvedRaceCodes.Count > 0)
            {
                var unresolved = string.Join(", ", unresolvedRaceCodes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException(
                    $"Canonical write requires pre-seeded races and could not resolve race ids for: {unresolved}.");
            }

            var existingDrivers = await dbContext.Drivers
                .AsNoTracking()
                .Where(x => !string.IsNullOrWhiteSpace(x.DriverId))
                .Select(x => new { DriverId = x.DriverId!, x.Code })
                .ToListAsync(cancellationToken);

            var driverIdByCode = existingDrivers
                .Where(x => !string.IsNullOrWhiteSpace(x.Code))
                .ToDictionary(
                    x => x.Code!.Trim().ToUpperInvariant(),
                    x => x.DriverId.Trim().ToLowerInvariant(),
                    StringComparer.OrdinalIgnoreCase);

            var driverIds = selections
                .SelectMany(x => ExtractDriverIds(x.NormalizedValue, driverIdByCode))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var existingDriverIdSet = existingDrivers
                .Select(x => x.DriverId)
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
                    Code = ResolveDriverCode(driverId)
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

            var calculatedPickScores = await dbContext.MigrationImportCalculatedScores
                .AsNoTracking()
                .Where(x => x.ImportRunId == runId)
                .ToListAsync(cancellationToken);

            var importedPickScores = await dbContext.MigrationImportLegacyPickScores
                .AsNoTracking()
                .Where(x => x.ImportRunId == runId)
                .ToListAsync(cancellationToken);

            var importedPickScoreByKey = importedPickScores
                .GroupBy(
                    x => new CanonicalPickScoreKey(x.RaceCode, x.PickType, x.Subject),
                    CanonicalPickScoreKey.Comparer)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(x => x.LegacyPoints.HasValue).First(),
                    CanonicalPickScoreKey.Comparer);

            var scopeKeysToWrite = incomingScopes
                .Select(scope => BuildSelectionKey(scope.RaceId, scope.Subject))
                .Where(key => !skippedSelectionKeys.Contains(key))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (scopeKeysToWrite.Count > 0)
            {
                var existingRacePickScores = await dbContext.RacePickScores
                    .Where(x => incomingRaceIds.Contains(x.RaceId) && incomingSubjects.Contains(x.ParticipantId))
                    .ToListAsync(cancellationToken);

                var scoresToRemove = existingRacePickScores
                    .Where(x => scopeKeysToWrite.Contains(BuildSelectionKey(x.RaceId, x.ParticipantId)))
                    .ToList();

                if (scoresToRemove.Count > 0)
                {
                    dbContext.RacePickScores.RemoveRange(scoresToRemove);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                var canonicalRacePickScores = calculatedPickScores
                    .Where(score => raceIdByCode.ContainsKey(score.RaceCode))
                    .Select(score =>
                    {
                        var raceId = raceIdByCode[score.RaceCode];
                        if (!scopeKeysToWrite.Contains(BuildSelectionKey(raceId, score.Subject)))
                        {
                            return null;
                        }

                        importedPickScoreByKey.TryGetValue(
                            new CanonicalPickScoreKey(score.RaceCode, score.PickType, score.Subject),
                            out var importedScore);

                        var importedPoints = importedScore?.LegacyPoints;
                        var calculatedPoints = decimal.ToInt32(decimal.Round(score.Points, 0, MidpointRounding.AwayFromZero));
                        int? overrideScore = importedPoints.HasValue && importedPoints.Value != calculatedPoints
                            ? importedPoints.Value
                            : null;

                        return new RacePickScoreEntity
                        {
                            RaceId = raceId,
                            RaceCode = score.RaceCode,
                            PickType = score.PickType,
                            ParticipantId = score.Subject,
                            PredictedValue = score.PredictedValue,
                            ActualValue = score.ActualValue,
                            ImportedPoints = importedPoints,
                            CalculatedPoints = calculatedPoints,
                            OverrideScore = overrideScore,
                            OverrideReasonCode = overrideScore.HasValue ? "MIGRATION_IMPORTED_OVERRIDE" : null,
                            SourceRunId = runId,
                            DeltaPoints = importedPoints.HasValue ? calculatedPoints - importedPoints.Value : 0,
                            ReasonCode = score.ReasonCode,
                            Explanation = null,
                            RecordedAtUtc = DateTime.UtcNow
                        };
                    })
                    .Where(score => score is not null)
                    .Cast<RacePickScoreEntity>()
                    .ToList();

                if (canonicalRacePickScores.Count != 0)
                {
                    await dbContext.RacePickScores.AddRangeAsync(canonicalRacePickScores, cancellationToken);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }

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
                        Driver = ExtractDriverIds(x.NormalizedValue, driverIdByCode).FirstOrDefault()
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

    private static IEnumerable<string> ExtractDriverIds(string? normalizedValue, IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return [];
        }

        return DriverTokenSplitRegex().Split(normalizedValue)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && x.Length <= 64)
            .Select(x => ResolveDriverIdToken(x, driverIdByCode))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string ResolveDriverIdToken(string token, IReadOnlyDictionary<string, string> driverIdByCode)
    {
        if (token.Length != 3)
        {
            return token.Trim().ToLowerInvariant();
        }

        var code = token.ToUpperInvariant();
        if (driverIdByCode.TryGetValue(code, out var mappedDriverId))
        {
            return mappedDriverId;
        }

        return JolpicaDriverIdByCode.TryGetValue(code, out var fallbackDriverId)
            ? fallbackDriverId
            : token;
    }

    private static string? ResolveDriverCode(string driverId)
    {
        var match = JolpicaDriverIdByCode.FirstOrDefault(x => string.Equals(x.Value, driverId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(match.Key))
        {
            return match.Key;
        }

        return driverId.Length <= 8
            ? driverId.ToUpperInvariant()
            : null;
    }

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

    private readonly record struct CanonicalPickScoreKey(string RaceCode, string PickType, string Subject)
    {
        public static IEqualityComparer<CanonicalPickScoreKey> Comparer { get; } = new CanonicalPickScoreKeyComparer();

        private sealed class CanonicalPickScoreKeyComparer : IEqualityComparer<CanonicalPickScoreKey>
        {
            public bool Equals(CanonicalPickScoreKey x, CanonicalPickScoreKey y)
            {
                return string.Equals(x.RaceCode, y.RaceCode, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.PickType, y.PickType, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Subject, y.Subject, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(CanonicalPickScoreKey obj)
            {
                return HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RaceCode),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PickType),
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Subject));
            }
        }
    }
}
