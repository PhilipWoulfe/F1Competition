using System.Text.RegularExpressions;
using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationLegacyScoreImporter : IMigrationLegacyScoreImporter
{
    private const string SectionTypeHeader = "Header";
    private const string SectionTypeRacePoints = "RacePoints";
    private const string SectionTypeTotalsMeta = "TotalsMeta";

    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;

    public MigrationLegacyScoreImporter(IDbContextFactory<F1DbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<MigrationLegacyScoreImportResult> ImportAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sourceFilePath = await dbContext.MigrationImportRuns
            .Where(x => x.Id == runId)
            .Select(x => x.SourceFilePath)
            .SingleOrDefaultAsync(cancellationToken);
        var usePhil2025SequenceMapping = !string.IsNullOrWhiteSpace(sourceFilePath) &&
            MigrationPhil2025CsvContractPolicy.AppliesTo(sourceFilePath);

        var stagedRows = await dbContext.MigrationImportRawRows
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var headerRow = stagedRows.FirstOrDefault(x => string.Equals(x.SectionType, SectionTypeHeader, StringComparison.Ordinal));
        var participants = ResolveParticipants(headerRow?.RawPayload);
        var mappedCircuitBySequence = await dbContext.MigrationImportRaceRoundMappings
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceSequence)
            .Select(x => new { x.RaceSequence, x.MappedCircuitId })
            .ToDictionaryAsync(x => x.RaceSequence, x => x.MappedCircuitId, cancellationToken);

        dbContext.MigrationImportLegacyPickScores.RemoveRange(
            dbContext.MigrationImportLegacyPickScores.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportImportedTotals.RemoveRange(
            dbContext.MigrationImportImportedTotals.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportCalculatedTotals.RemoveRange(
            dbContext.MigrationImportCalculatedTotals.Where(x => x.ImportRunId == runId));

        if (participants.Count == 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new MigrationLegacyScoreImportResult(0, 0, 0);
        }

        var legacyPickScores = new List<MigrationImportLegacyPickScoreEntity>();
        string? currentRaceCode = null;
        string? currentCanonicalRaceCode = null;
        var raceSequence = 0;

        foreach (var row in stagedRows.Where(x => string.Equals(x.SectionType, SectionTypeRacePoints, StringComparison.Ordinal)))
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            if (!TryResolveRace(columns[0], ref currentRaceCode, out var raceCode, out var pickType))
            {
                continue;
            }

            if (string.Equals(pickType, "1", StringComparison.OrdinalIgnoreCase))
            {
                raceSequence++;

                currentCanonicalRaceCode = usePhil2025SequenceMapping
                    ? MigrationPhil2025RaceSequenceMapper.TryResolveCircuitId(raceSequence) ?? raceCode
                    : raceCode;
            }

            var fallbackRaceCode = usePhil2025SequenceMapping
                ? currentCanonicalRaceCode ?? raceCode
                : raceCode;

            var mappedRaceCode = mappedCircuitBySequence.TryGetValue(raceSequence, out var mappedCircuitId) && !string.IsNullOrWhiteSpace(mappedCircuitId)
                ? mappedCircuitId
                : fallbackRaceCode;

            var participantValues = columns.Skip(1).Take(participants.Count).ToArray();
            for (var index = 0; index < participants.Count; index++)
            {
                var raw = index < participantValues.Length ? participantValues[index]?.Trim() : null;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                int? points = int.TryParse(raw, out var parsed) ? parsed : null;
                legacyPickScores.Add(new MigrationImportLegacyPickScoreEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = mappedRaceCode,
                    PickType = pickType,
                    Subject = participants[index],
                    RawLegacyPoints = raw,
                    LegacyPoints = points
                });
            }
        }

        var importedTotalsBySubject = new Dictionary<string, MigrationImportImportedTotalEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in stagedRows.Where(x => string.Equals(x.SectionType, SectionTypeTotalsMeta, StringComparison.Ordinal)))
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0 || !columns[0].Trim().Equals("Result", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var participantValues = columns.Skip(1).Take(participants.Count).ToArray();
            for (var index = 0; index < participants.Count; index++)
            {
                var raw = index < participantValues.Length ? participantValues[index]?.Trim() : null;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                importedTotalsBySubject[participants[index]] = new MigrationImportImportedTotalEntity
                {
                    ImportRunId = runId,
                    Subject = participants[index],
                    RawTotal = raw,
                    ImportedTotalPoints = int.TryParse(raw, out var parsed) ? parsed : null
                };
            }
        }

        var calculatedTotals = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .GroupBy(x => x.Subject)
            .Select(group => new MigrationImportCalculatedTotalEntity
            {
                ImportRunId = runId,
                Subject = group.Key,
                CalculatedTotalPoints = group.Sum(x => x.Points)
            })
            .ToListAsync(cancellationToken);

        if (legacyPickScores.Count > 0)
        {
            await dbContext.MigrationImportLegacyPickScores.AddRangeAsync(legacyPickScores, cancellationToken);
        }

        if (importedTotalsBySubject.Count > 0)
        {
            await dbContext.MigrationImportImportedTotals.AddRangeAsync(importedTotalsBySubject.Values, cancellationToken);
        }

        if (calculatedTotals.Count > 0)
        {
            await dbContext.MigrationImportCalculatedTotals.AddRangeAsync(calculatedTotals, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationLegacyScoreImportResult(
            LegacyPickScoreCount: legacyPickScores.Count,
            ImportedTotalCount: importedTotalsBySubject.Count,
            CalculatedTotalCount: calculatedTotals.Count);
    }

    private static List<string> ResolveParticipants(string? headerPayload)
    {
        if (string.IsNullOrWhiteSpace(headerPayload))
        {
            return [];
        }

        var columns = CsvLineParser.Parse(headerPayload);
        if (columns.Count < 2)
        {
            return [];
        }

        var participants = new List<string>();
        foreach (var token in columns.Skip(1))
        {
            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                break;
            }

            participants.Add(trimmed);
        }

        return participants;
    }

    private static bool TryResolveRace(
        string label,
        ref string? currentRaceCode,
        out string raceCode,
        out string pickType)
    {
        raceCode = string.Empty;
        pickType = string.Empty;

        if (string.IsNullOrWhiteSpace(label))
        {
            return false;
        }

        var normalizedLabel = label.Trim();
        if (normalizedLabel.StartsWith("L-", StringComparison.OrdinalIgnoreCase))
        {
            normalizedLabel = normalizedLabel[2..];
        }

        if (normalizedLabel.Equals("DNF", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(currentRaceCode))
            {
                return false;
            }

            raceCode = currentRaceCode;
            pickType = "DNF";
            return true;
        }

        if (normalizedLabel.Equals("BAH-HUMBUG", StringComparison.OrdinalIgnoreCase) ||
            normalizedLabel.StartsWith("BAK-", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(currentRaceCode))
            {
                return false;
            }

            raceCode = currentRaceCode;
            pickType = "DNF";
            return true;
        }

        var match = RaceLabelRegex().Match(normalizedLabel);
        if (!match.Success)
        {
            return false;
        }

        raceCode = RaceCodeNormalizer.NormalizeRaceCode(match.Groups[1].Value);
        pickType = match.Groups[2].Value.ToUpperInvariant();
        currentRaceCode = raceCode;
        return true;
    }

    [GeneratedRegex("^([A-Za-z][A-Za-z\\s]{2,})-(1|2|3|DNF)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RaceLabelRegex();
}