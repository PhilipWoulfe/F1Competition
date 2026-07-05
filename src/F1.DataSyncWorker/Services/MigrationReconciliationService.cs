using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.DataSyncWorker.Services;

public sealed class MigrationReconciliationService : IMigrationReconciliationService
{
    private static readonly HashSet<string> PodiumPickTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1",
        "2",
        "3"
    };

    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;

    public MigrationReconciliationService(IDbContextFactory<F1DbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<MigrationReconciliationResult> ReconcileAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        dbContext.MigrationImportPickDiffs.RemoveRange(
            dbContext.MigrationImportPickDiffs.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportRaceDiffs.RemoveRange(
            dbContext.MigrationImportRaceDiffs.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportParticipantDeltaSummaries.RemoveRange(
            dbContext.MigrationImportParticipantDeltaSummaries.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportReasonCategorySummaries.RemoveRange(
            dbContext.MigrationImportReasonCategorySummaries.Where(x => x.ImportRunId == runId));

        var legacy = await dbContext.MigrationImportLegacyPickScores
            .Where(x => x.ImportRunId == runId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var calculated = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var legacyByKey = legacy
            .GroupBy(x => new PickDiffKey(x.RaceCode, x.PickType, x.Subject), PickDiffKeyComparer.Instance)
            .ToDictionary(
                x => x.Key,
                x => x.Any(y => y.LegacyPoints is null) ? (int?)null : x.Sum(y => y.LegacyPoints ?? 0),
                PickDiffKeyComparer.Instance);

        var calculatedByKey = calculated
            .GroupBy(x => new PickDiffKey(x.RaceCode, x.PickType, x.Subject), PickDiffKeyComparer.Instance)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(y => y.Points),
                PickDiffKeyComparer.Instance);

        var allKeys = legacyByKey.Keys
            .Concat(calculatedByKey.Keys)
            .Distinct(PickDiffKeyComparer.Instance)
            .OrderBy(x => x.RaceCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Subject, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => PickTypeOrder(x.PickType))
            .ThenBy(x => x.PickType, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var pickDiffs = new List<MigrationImportPickDiffEntity>(allKeys.Count);
        foreach (var key in allKeys)
        {
            var hasImported = legacyByKey.TryGetValue(key, out var importedPoints);
            var hasCalculated = calculatedByKey.TryGetValue(key, out var calculatedPoints);

            int? imported = hasImported ? importedPoints : null;
            int? calculatedValue = hasCalculated ? calculatedPoints : null;
            var delta = (calculatedValue ?? 0) - (imported ?? 0);

            var reasonCode = ResolveReasonCode(key.PickType, imported, calculatedValue, delta);
            var explanation = BuildPickExplanation(key, imported, calculatedValue, delta, reasonCode);

            pickDiffs.Add(new MigrationImportPickDiffEntity
            {
                ImportRunId = runId,
                RaceCode = key.RaceCode,
                PickType = key.PickType,
                Subject = key.Subject,
                ImportedPoints = imported,
                CalculatedPoints = calculatedValue,
                DeltaPoints = delta,
                ReasonCode = reasonCode,
                Explanation = explanation
            });
        }

        var raceDiffs = pickDiffs
            .GroupBy(x => new RaceDiffKey(x.RaceCode, x.Subject), RaceDiffKeyComparer.Instance)
            .OrderBy(x => x.Key.RaceCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key.Subject, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var importedPoints = group.Sum(x => x.ImportedPoints ?? 0);
                var calculatedPoints = group.Sum(x => x.CalculatedPoints ?? 0);
                var delta = calculatedPoints - importedPoints;

                var topReason = group
                    .Where(x => x.DeltaPoints != 0)
                    .GroupBy(x => x.ReasonCode, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.Key)
                    .FirstOrDefault() ?? "RACE_POINTS_MATCH";

                return new MigrationImportRaceDiffEntity
                {
                    ImportRunId = runId,
                    RaceCode = group.Key.RaceCode,
                    Subject = group.Key.Subject,
                    ImportedPoints = importedPoints,
                    CalculatedPoints = calculatedPoints,
                    DeltaPoints = delta,
                    ReasonCode = topReason,
                    Explanation = $"{group.Key.Subject} {group.Key.RaceCode} imported {importedPoints}, calculated {calculatedPoints}, delta {delta}."
                };
            })
            .ToList();

        var participantSummaries = raceDiffs
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

                return new MigrationImportParticipantDeltaSummaryEntity
                {
                    ImportRunId = runId,
                    Subject = group.Key,
                    ImportedTotalPoints = group.Sum(x => x.ImportedPoints),
                    CalculatedTotalPoints = group.Sum(x => x.CalculatedPoints),
                    NetDeltaPoints = group.Sum(x => x.DeltaPoints),
                    TopReasonCode = topReasonGroup?.Key,
                    TopReasonCount = topReasonGroup?.Count() ?? 0
                };
            })
            .ToList();

        var reasonSummaries = pickDiffs
            .Where(x => x.DeltaPoints != 0)
            .GroupBy(x => x.ReasonCode, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new MigrationImportReasonCategorySummaryEntity
            {
                ImportRunId = runId,
                ReasonCode = group.Key,
                OccurrenceCount = group.Count(),
                TotalDeltaPoints = group.Sum(x => x.DeltaPoints)
            })
            .ToList();

        if (pickDiffs.Count > 0)
        {
            await dbContext.MigrationImportPickDiffs.AddRangeAsync(pickDiffs, cancellationToken);
        }

        if (raceDiffs.Count > 0)
        {
            await dbContext.MigrationImportRaceDiffs.AddRangeAsync(raceDiffs, cancellationToken);
        }

        if (participantSummaries.Count > 0)
        {
            await dbContext.MigrationImportParticipantDeltaSummaries.AddRangeAsync(participantSummaries, cancellationToken);
        }

        if (reasonSummaries.Count > 0)
        {
            await dbContext.MigrationImportReasonCategorySummaries.AddRangeAsync(reasonSummaries, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationReconciliationResult(
            PickDiffCount: pickDiffs.Count,
            RaceDiffCount: raceDiffs.Count,
            ParticipantSummaryCount: participantSummaries.Count,
            ReasonSummaryCount: reasonSummaries.Count,
            TotalDelta: pickDiffs.Sum(x => x.DeltaPoints));
    }

    private static string ResolveReasonCode(string pickType, int? imported, int? calculated, int delta)
    {
        if (!imported.HasValue)
        {
            return "LEGACY_POINTS_MISSING";
        }

        if (!calculated.HasValue)
        {
            return "CALCULATED_POINTS_MISSING";
        }

        if (delta == 0)
        {
            return "POINTS_MATCH";
        }

        if (string.Equals(pickType, "DNF", StringComparison.OrdinalIgnoreCase))
        {
            return "DNF_RULE_VARIANCE";
        }

        if (PodiumPickTypes.Contains(pickType))
        {
            return "PODIUM_RULE_VARIANCE";
        }

        return "RULE_VARIANCE";
    }

    private static string BuildPickExplanation(PickDiffKey key, int? imported, int? calculated, int delta, string reasonCode)
    {
        if (string.Equals(reasonCode, "POINTS_MATCH", StringComparison.Ordinal))
        {
            return $"{key.Subject} {key.RaceCode}-{key.PickType} imported and calculated points match at {calculated ?? imported ?? 0}.";
        }

        return $"{key.Subject} {key.RaceCode}-{key.PickType} imported {imported?.ToString() ?? "missing"}, calculated {calculated?.ToString() ?? "missing"}, delta {delta}. Reason: {reasonCode}.";
    }

    private static int PickTypeOrder(string pickType)
    {
        return pickType.ToUpperInvariant() switch
        {
            "1" => 1,
            "2" => 2,
            "3" => 3,
            "DNF" => 4,
            _ => 99
        };
    }

    private sealed record PickDiffKey(string RaceCode, string PickType, string Subject);

    private sealed class PickDiffKeyComparer : IEqualityComparer<PickDiffKey>
    {
        public static readonly PickDiffKeyComparer Instance = new();

        public bool Equals(PickDiffKey? x, PickDiffKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.RaceCode, y.RaceCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.PickType, y.PickType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Subject, y.Subject, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(PickDiffKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RaceCode),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PickType),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Subject));
        }
    }

    private sealed record RaceDiffKey(string RaceCode, string Subject);

    private sealed class RaceDiffKeyComparer : IEqualityComparer<RaceDiffKey>
    {
        public static readonly RaceDiffKeyComparer Instance = new();

        public bool Equals(RaceDiffKey? x, RaceDiffKey? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.RaceCode, y.RaceCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Subject, y.Subject, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(RaceDiffKey obj)
        {
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RaceCode),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Subject));
        }
    }
}
