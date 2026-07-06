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

        var legacyRowsByKey = legacy
            .GroupBy(x => new PickDiffKey(x.RaceCode, x.PickType, x.Subject), PickDiffKeyComparer.Instance)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => y.RowNumber).Distinct().OrderBy(y => y).ToArray(),
                PickDiffKeyComparer.Instance);

        var calculatedByKey = calculated
            .GroupBy(x => new PickDiffKey(x.RaceCode, x.PickType, x.Subject), PickDiffKeyComparer.Instance)
            .ToDictionary(
                x => x.Key,
                x => x.Sum(y => y.Points),
                PickDiffKeyComparer.Instance);

        var calculatedRowsByKey = calculated
            .GroupBy(x => new PickDiffKey(x.RaceCode, x.PickType, x.Subject), PickDiffKeyComparer.Instance)
            .ToDictionary(
                x => x.Key,
                x => x.Select(y => y.RowNumber).Distinct().OrderBy(y => y).ToArray(),
                PickDiffKeyComparer.Instance);

        var participantColumnBySubject = await ResolveParticipantColumnsBySubjectAsync(dbContext, runId, cancellationToken);

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

            var importedRows = legacyRowsByKey.GetValueOrDefault(key, []);
            var calculatedRows = calculatedRowsByKey.GetValueOrDefault(key, []);
            participantColumnBySubject.TryGetValue(key.Subject, out var participantColumn);

            var reasonCode = ResolveReasonCode(key.PickType, imported, calculatedValue, delta);
            var explanation = BuildPickExplanation(
                key,
                imported,
                calculatedValue,
                delta,
                reasonCode,
                importedRows,
                calculatedRows,
                participantColumn);

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
                    Explanation = BuildRaceExplanation(
                        group.Key.RaceCode,
                        group.Key.Subject,
                        importedPoints,
                        calculatedPoints,
                        delta,
                        group,
                        legacyRowsByKey,
                        calculatedRowsByKey,
                        participantColumnBySubject)
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

    private static string BuildPickExplanation(
        PickDiffKey key,
        int? imported,
        int? calculated,
        int delta,
        string reasonCode,
        IReadOnlyList<int> importedRows,
        IReadOnlyList<int> calculatedRows,
        string? participantColumn)
    {
        var importedSource = FormatSourceReference(importedRows, participantColumn);
        var calculatedSource = FormatSourceReference(calculatedRows, participantColumn);

        if (string.Equals(reasonCode, "POINTS_MATCH", StringComparison.Ordinal))
        {
            return $"{key.Subject} {key.RaceCode}-{key.PickType} imported and calculated points match at {calculated ?? imported ?? 0} ({calculatedSource}).";
        }

        return $"{key.Subject} {key.RaceCode}-{key.PickType} imported {imported?.ToString() ?? "missing"} ({importedSource}), calculated {calculated?.ToString() ?? "missing"} ({calculatedSource}), delta {delta}. Reason: {reasonCode}.";
    }

    private static string BuildRaceExplanation(
        string raceCode,
        string subject,
        int importedPoints,
        int calculatedPoints,
        int delta,
        IEnumerable<MigrationImportPickDiffEntity> pickDiffs,
        IReadOnlyDictionary<PickDiffKey, int[]> legacyRowsByKey,
        IReadOnlyDictionary<PickDiffKey, int[]> calculatedRowsByKey,
        IReadOnlyDictionary<string, string> participantColumnBySubject)
    {
        var contributors = pickDiffs
            .Where(x => x.DeltaPoints != 0)
            .OrderBy(x => PickTypeOrder(x.PickType))
            .ThenBy(x => x.PickType, StringComparer.OrdinalIgnoreCase)
            .Select(x =>
            {
                var key = new PickDiffKey(x.RaceCode, x.PickType, x.Subject);
                var importedRows = legacyRowsByKey.GetValueOrDefault(key, []);
                var calculatedRows = calculatedRowsByKey.GetValueOrDefault(key, []);
                participantColumnBySubject.TryGetValue(x.Subject, out var participantColumn);

                return $"{raceCode}-{x.PickType} {x.ImportedPoints?.ToString() ?? "missing"}->{x.CalculatedPoints?.ToString() ?? "missing"} ({x.DeltaPoints}) [{x.ReasonCode}] [imported {FormatSourceReference(importedRows, participantColumn)}; calculated {FormatSourceReference(calculatedRows, participantColumn)}]";
            })
            .ToList();

        var suffix = contributors.Count == 0
            ? "No pick-level variance."
            : $"Contributors: {string.Join("; ", contributors)}.";

        var explanation = $"{subject} {raceCode} imported {importedPoints}, calculated {calculatedPoints}, delta {delta}. {suffix}";
        return explanation.Length <= 1024 ? explanation : explanation[..1021] + "...";
    }

    private static string FormatSourceReference(IReadOnlyList<int> rows, string? column)
    {
        var rowText = rows.Count switch
        {
            0 => "row n/a",
            1 => $"row {rows[0]}",
            _ => $"rows {string.Join(",", rows)}"
        };

        var columnText = string.IsNullOrWhiteSpace(column)
            ? "column ?"
            : $"column {column}";

        return $"{rowText}, {columnText}";
    }

    private static async Task<Dictionary<string, string>> ResolveParticipantColumnsBySubjectAsync(
        F1DbContext dbContext,
        Guid runId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var headerRow = await dbContext.MigrationImportRawRows
            .AsNoTracking()
            .Where(x => x.ImportRunId == runId && x.SectionType == MigrationImportSectionTypes.Header)
            .OrderBy(x => x.RowNumber)
            .Select(x => x.RawPayload)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(headerRow))
        {
            return map;
        }

        var columns = ParseCsvLine(headerRow);
        var participantCount = 0;

        for (var index = 1; index < columns.Count; index++)
        {
            var participant = columns[index].Trim();
            if (participant.Length == 0)
            {
                break;
            }

            participantCount++;
            map[participant] = ToExcelColumnName(index + 1);
        }

        if (participantCount > 0)
        {
            map["ACTUAL"] = ToExcelColumnName(participantCount + 2);
        }

        return map;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static string ToExcelColumnName(int columnNumber)
    {
        if (columnNumber <= 0)
        {
            return "?";
        }

        var chars = new Stack<char>();
        var current = columnNumber;

        while (current > 0)
        {
            current--;
            chars.Push((char)('A' + (current % 26)));
            current /= 26;
        }

        return new string(chars.ToArray());
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
