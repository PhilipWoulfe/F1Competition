using System.Text.RegularExpressions;
using System.Globalization;
using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Options;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationLegacyScoreImporter : IMigrationLegacyScoreImporter
{
    private const string SectionTypeHeader = "Header";
    private const string SectionTypeRacePoints = "RacePoints";
    private const string SectionTypeSeasonQuestionPoints = "SeasonQuestionPoints";
    private const string SectionTypeTotalsMeta = "TotalsMeta";
    private const string DaveLeaderboardRaceCode = "LEADERBOARD";
    private const string DaveLeaderboardRaceTotalPickType = "RACE_TOTAL";
    private const string DaveLeaderboardBonusTotalPickType = "BONUS_TOTAL";
    private const string DaveLeaderboardCdpPickType = "CDP";
    private const int DaveDefaultPreseasonPointsPerQuestion = 30;

    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;
    private readonly MigrationImportOptions _importOptions;

    public MigrationLegacyScoreImporter(
        IDbContextFactory<F1DbContext> dbContextFactory,
        IOptions<MigrationImportOptions> importOptions)
    {
        _dbContextFactory = dbContextFactory;
        _importOptions = importOptions.Value;
    }

    public MigrationLegacyScoreImporter(IDbContextFactory<F1DbContext> dbContextFactory)
        : this(dbContextFactory, Microsoft.Extensions.Options.Options.Create(new MigrationImportOptions()))
    {
    }

    public async Task<MigrationLegacyScoreImportResult> ImportAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sourceFilePath = await dbContext.MigrationImportRuns
            .Where(x => x.Id == runId)
            .Select(x => x.SourceFilePath)
            .SingleOrDefaultAsync(cancellationToken);
        var sourceProfile = MigrationSourceProfileResolver.Resolve(sourceFilePath ?? string.Empty);
        var usePhil2025SequenceMapping = sourceProfile == MigrationSourceProfile.Phil2025Csv;

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
        dbContext.MigrationImportPreseasonPolicies.RemoveRange(
            dbContext.MigrationImportPreseasonPolicies.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportPreseasonImportedTallies.RemoveRange(
            dbContext.MigrationImportPreseasonImportedTallies.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportImportedTotals.RemoveRange(
            dbContext.MigrationImportImportedTotals.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportCalculatedTotals.RemoveRange(
            dbContext.MigrationImportCalculatedTotals.Where(x => x.ImportRunId == runId));

        if (sourceProfile == MigrationSourceProfile.Dave2025Package)
        {
            var daveImport = ParseDaveLeaderboardImport(runId, stagedRows);
            var davePreseasonPolicy = new MigrationImportPreseasonPolicyEntity
            {
                ImportRunId = runId,
                RowNumber = 0,
                ColumnIndex = 0,
                CellReference = "DaveDefault",
                RawPointsPerQuestion = DaveDefaultPreseasonPointsPerQuestion.ToString(CultureInfo.InvariantCulture),
                PointsPerQuestion = DaveDefaultPreseasonPointsPerQuestion
            };

            await dbContext.MigrationImportPreseasonPolicies.AddAsync(davePreseasonPolicy, cancellationToken);

            if (daveImport.LegacyPickScores.Count > 0)
            {
                await dbContext.MigrationImportLegacyPickScores.AddRangeAsync(daveImport.LegacyPickScores, cancellationToken);
            }

            if (daveImport.ImportedTotals.Count > 0)
            {
                await dbContext.MigrationImportImportedTotals.AddRangeAsync(daveImport.ImportedTotals.Values, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            return new MigrationLegacyScoreImportResult(
                LegacyPickScoreCount: daveImport.LegacyPickScores.Count,
                ImportedTotalCount: daveImport.ImportedTotals.Count,
                CalculatedTotalCount: 0);
        }

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

        var preseasonPolicy = ParsePreseasonPolicy(runId, stagedRows, usePhil2025SequenceMapping);
        var preseasonTallies = ParsePreseasonImportedTallies(runId, stagedRows, participants, usePhil2025SequenceMapping);

        if (legacyPickScores.Count > 0)
        {
            await dbContext.MigrationImportLegacyPickScores.AddRangeAsync(legacyPickScores, cancellationToken);
        }

        if (preseasonPolicy is not null)
        {
            await dbContext.MigrationImportPreseasonPolicies.AddAsync(preseasonPolicy, cancellationToken);
        }

        if (preseasonTallies.Count > 0)
        {
            await dbContext.MigrationImportPreseasonImportedTallies.AddRangeAsync(preseasonTallies, cancellationToken);
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

    private MigrationImportPreseasonPolicyEntity? ParsePreseasonPolicy(
        Guid runId,
        IReadOnlyCollection<MigrationImportRawRowEntity> stagedRows,
        bool usePhil2025Contract)
    {
        if (!usePhil2025Contract)
        {
            return null;
        }

        var policyRow = stagedRows.FirstOrDefault(x => x.RowNumber == MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyRow);
        if (policyRow is null)
        {
            HandlePreseasonPolicyParseIssue(
                $"Missing preseason policy cell M2: row {MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyRow} not found.");
            return new MigrationImportPreseasonPolicyEntity
            {
                ImportRunId = runId,
                RowNumber = MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyRow,
                ColumnIndex = MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyColumnIndex,
                CellReference = "M2",
                RawPointsPerQuestion = null,
                PointsPerQuestion = null
            };
        }

        var columns = CsvLineParser.Parse(policyRow.RawPayload);
        var raw = MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyColumnIndex < columns.Count
            ? columns[MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyColumnIndex].Trim()
            : null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            HandlePreseasonPolicyParseIssue(
                $"Missing preseason policy value at M2 (row {policyRow.RowNumber}, column M).",
                policyRow.RowNumber);
            return new MigrationImportPreseasonPolicyEntity
            {
                ImportRunId = runId,
                RowNumber = policyRow.RowNumber,
                ColumnIndex = MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyColumnIndex,
                CellReference = "M2",
                RawPointsPerQuestion = null,
                PointsPerQuestion = null
            };
        }

        if (!int.TryParse(raw, out var parsedPoints))
        {
            HandlePreseasonPolicyParseIssue(
                $"Malformed preseason policy value at M2 (row {policyRow.RowNumber}): '{raw}' is not an integer.",
                policyRow.RowNumber);
            return new MigrationImportPreseasonPolicyEntity
            {
                ImportRunId = runId,
                RowNumber = policyRow.RowNumber,
                ColumnIndex = MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyColumnIndex,
                CellReference = "M2",
                RawPointsPerQuestion = raw,
                PointsPerQuestion = null
            };
        }

        return new MigrationImportPreseasonPolicyEntity
        {
            ImportRunId = runId,
            RowNumber = policyRow.RowNumber,
            ColumnIndex = MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyColumnIndex,
            CellReference = "M2",
            RawPointsPerQuestion = raw,
            PointsPerQuestion = parsedPoints
        };
    }

    private List<MigrationImportPreseasonImportedTallyEntity> ParsePreseasonImportedTallies(
        Guid runId,
        IReadOnlyCollection<MigrationImportRawRowEntity> stagedRows,
        IReadOnlyList<string> participants,
        bool usePhil2025Contract)
    {
        if (!usePhil2025Contract || participants.Count == 0)
        {
            return [];
        }

        var preseasonRows = stagedRows
            .Where(x => string.Equals(x.SectionType, SectionTypeSeasonQuestionPoints, StringComparison.Ordinal))
            .OrderBy(x => x.RowNumber)
            .ToList();

        var parsed = new List<MigrationImportPreseasonImportedTallyEntity>();
        var participantStartColumnIndex = MigrationPhil2025CsvContractPolicy.ParticipantStartColumnIndex;

        foreach (var row in preseasonRows)
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            var questionText = columns[0].Trim();
            var questionKey = ResolvePreseasonQuestionKey(row.RowNumber, usePhil2025Contract);

            for (var participantIndex = 0; participantIndex < participants.Count; participantIndex++)
            {
                var columnIndex = participantStartColumnIndex + participantIndex;
                var raw = columnIndex < columns.Count ? columns[columnIndex].Trim() : null;

                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                if (!int.TryParse(raw, out var parsedPoints))
                {
                    HandlePreseasonTallyParseIssue(
                        $"Malformed preseason tally at row {row.RowNumber}, column {ToExcelColumnName(columnIndex)} for '{participants[participantIndex]}': '{raw}' is not an integer.",
                        row.RowNumber);
                }

                parsed.Add(new MigrationImportPreseasonImportedTallyEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    QuestionKey = questionKey,
                    QuestionText = questionText,
                    Subject = participants[participantIndex],
                    RawPoints = raw,
                    ImportedPoints = int.TryParse(raw, out var points) ? points : null
                });
            }
        }

        return parsed;
    }

    private static string ResolvePreseasonQuestionKey(int rowNumber, bool usePhil2025Contract)
    {
        var normalizedRowNumber = rowNumber;
        if (usePhil2025Contract)
        {
            var contractOffset = MigrationPhil2025CsvContractPolicy.PreseasonPointsStartRow -
                MigrationPhil2025CsvContractPolicy.PreseasonQuestionStartRow;
            normalizedRowNumber = Math.Max(1, rowNumber - contractOffset);
        }

        return $"PRE-{normalizedRowNumber:D3}";
    }

    private void HandlePreseasonPolicyParseIssue(string message, int? rowNumber = null)
    {
        if (_importOptions.FailOnPreseasonPolicyParseError)
        {
            throw rowNumber is null
                ? new InvalidOperationException($"Preseason policy parse failed. {message}")
                : new InvalidOperationException($"Preseason policy parse failed at row {rowNumber}. {message}");
        }
    }

    private void HandlePreseasonTallyParseIssue(string message, int rowNumber)
    {
        if (_importOptions.FailOnPreseasonTallyParseError)
        {
            throw new InvalidOperationException($"Preseason tally parse failed at row {rowNumber}. {message}");
        }
    }

    private static string ToExcelColumnName(int zeroBasedIndex)
    {
        var index = zeroBasedIndex + 1;
        var result = string.Empty;

        while (index > 0)
        {
            var remainder = (index - 1) % 26;
            result = (char)('A' + remainder) + result;
            index = (index - 1) / 26;
        }

        return result;
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

        if (normalizedLabel.Equals("BAH-HUMBUG", StringComparison.OrdinalIgnoreCase))
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

    private static (List<MigrationImportLegacyPickScoreEntity> LegacyPickScores, Dictionary<string, MigrationImportImportedTotalEntity> ImportedTotals)
        ParseDaveLeaderboardImport(
            Guid runId,
            IReadOnlyCollection<MigrationImportRawRowEntity> stagedRows)
    {
        var leaderboardRows = stagedRows
            .Where(x => string.Equals(x.SourceFileName, Dave2025SourcePackageContract.LeaderboardFile, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.RowNumber)
            .ToList();

        if (leaderboardRows.Count == 0)
        {
            return ([], new Dictionary<string, MigrationImportImportedTotalEntity>(StringComparer.OrdinalIgnoreCase));
        }

        var header = CsvLineParser.Parse(leaderboardRows[0].RawPayload);
        if (header.Count == 0)
        {
            return ([], new Dictionary<string, MigrationImportImportedTotalEntity>(StringComparer.OrdinalIgnoreCase));
        }

        var nameColumnIndex = FindColumnIndex(header, "name", "participant", "player");
        if (nameColumnIndex < 0)
        {
            nameColumnIndex = 0;
        }

        var racePointsColumnIndex = FindColumnIndex(header, "racepoints", "racepoint", "race");
        var bonusPointsColumnIndex = FindColumnIndex(header, "bonuspoints", "bonuspoint", "bonus", "preseason");
        var totalColumnIndex = FindColumnIndex(header, "total", "points");
        var finalColumnIndex = FindColumnIndex(header, "final", "finaltotal", "finalpoints");
        var cdpColumnIndex = FindColumnIndex(header, "cdp", "correctpodiumpicks", "correctpodium");

        var legacyPickScores = new List<MigrationImportLegacyPickScoreEntity>();
        var importedTotals = new Dictionary<string, MigrationImportImportedTotalEntity>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in leaderboardRows.Skip(1))
        {
            var columns = CsvLineParser.Parse(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            var subject = nameColumnIndex < columns.Count ? columns[nameColumnIndex].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(subject) || string.Equals(subject, "Result", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddComponentScore(legacyPickScores, runId, row.RowNumber, subject, columns, racePointsColumnIndex, DaveLeaderboardRaceTotalPickType);
            AddComponentScore(legacyPickScores, runId, row.RowNumber, subject, columns, bonusPointsColumnIndex, DaveLeaderboardBonusTotalPickType);
            AddComponentScore(legacyPickScores, runId, row.RowNumber, subject, columns, cdpColumnIndex, DaveLeaderboardCdpPickType);

            var finalRaw = ReadColumn(columns, finalColumnIndex);
            var totalRaw = ReadColumn(columns, totalColumnIndex);
            var chosenTotalRaw = !string.IsNullOrWhiteSpace(finalRaw) ? finalRaw : totalRaw;
            if (string.IsNullOrWhiteSpace(chosenTotalRaw))
            {
                continue;
            }

            importedTotals[subject] = new MigrationImportImportedTotalEntity
            {
                ImportRunId = runId,
                Subject = subject,
                RawTotal = chosenTotalRaw,
                ImportedTotalPoints = TryParseScore(chosenTotalRaw)
            };
        }

        return (legacyPickScores, importedTotals);
    }

    private static void AddComponentScore(
        ICollection<MigrationImportLegacyPickScoreEntity> target,
        Guid runId,
        int rowNumber,
        string subject,
        IReadOnlyList<string> columns,
        int columnIndex,
        string pickType)
    {
        if (columnIndex < 0)
        {
            return;
        }

        var raw = ReadColumn(columns, columnIndex);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        target.Add(new MigrationImportLegacyPickScoreEntity
        {
            ImportRunId = runId,
            RowNumber = rowNumber,
            RaceCode = DaveLeaderboardRaceCode,
            PickType = pickType,
            Subject = subject,
            RawLegacyPoints = raw,
            LegacyPoints = TryParseScore(raw)
        });
    }

    private static string? ReadColumn(IReadOnlyList<string> columns, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= columns.Count)
        {
            return null;
        }

        var value = columns[columnIndex].Trim();
        return value.Length == 0 ? null : value;
    }

    private static int FindColumnIndex(IReadOnlyList<string> header, params string[] normalizedCandidates)
    {
        for (var index = 0; index < header.Count; index++)
        {
            var normalized = NormalizeHeaderToken(header[index]);
            if (normalizedCandidates.Any(candidate => string.Equals(normalized, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        for (var index = 0; index < header.Count; index++)
        {
            var normalized = NormalizeHeaderToken(header[index]);
            if (normalizedCandidates.Any(candidate => normalized.Contains(candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return index;
            }
        }

        return -1;
    }

    private static string NormalizeHeaderToken(string raw)
    {
        var compact = raw.Trim().ToLowerInvariant();
        return new string(compact.Where(char.IsLetterOrDigit).ToArray());
    }

    private static int? TryParseScore(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInteger))
        {
            return parsedInteger;
        }

        if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedDecimal) ||
            decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out parsedDecimal))
        {
            return (int)Math.Round(parsedDecimal, MidpointRounding.AwayFromZero);
        }

        return null;
    }
}