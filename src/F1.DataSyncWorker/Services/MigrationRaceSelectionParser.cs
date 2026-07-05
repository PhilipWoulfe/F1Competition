using System.Text;
using System.Text.RegularExpressions;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationRaceSelectionParser : IMigrationRaceSelectionParser
{
    private const string SectionTypeRacePick = "RacePick";
    private const string SectionTypeHeader = "Header";
    private const string ActualSubject = "ACTUAL";
    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;

    public MigrationRaceSelectionParser(IDbContextFactory<F1DbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<int> ParseAndPersistAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stagedRows = await dbContext.MigrationImportRawRows
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var headerRow = stagedRows.FirstOrDefault(x => string.Equals(x.SectionType, SectionTypeHeader, StringComparison.Ordinal));
        var participants = ResolveParticipants(headerRow?.RawPayload);

        if (participants.Count == 0)
        {
            return 0;
        }

        var raceRows = stagedRows.Where(x => string.Equals(x.SectionType, SectionTypeRacePick, StringComparison.Ordinal));
        var selections = new List<MigrationImportRaceSelectionEntity>();
        string? currentRaceCode = null;

        foreach (var row in raceRows)
        {
            var columns = ParseCsvLine(row.RawPayload);
            if (columns.Count == 0)
            {
                continue;
            }

            var label = columns[0].Trim();
            if (!TryResolveRace(label, ref currentRaceCode, out var raceCode, out var pickType))
            {
                continue;
            }

            var participantValues = columns.Skip(1).Take(participants.Count).ToArray();
            for (var index = 0; index < participants.Count; index++)
            {
                var rawValue = index < participantValues.Length ? participantValues[index] : string.Empty;
                selections.Add(new MigrationImportRaceSelectionEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = raceCode,
                    PickType = pickType,
                    Subject = participants[index],
                    RawValue = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim(),
                    NormalizedValue = NormalizeSelection(rawValue),
                    IsActualOutcome = false
                });
            }

            var actualRaw = columns.Skip(1 + participants.Count).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            selections.Add(new MigrationImportRaceSelectionEntity
            {
                ImportRunId = runId,
                RowNumber = row.RowNumber,
                RaceCode = raceCode,
                PickType = pickType,
                Subject = ActualSubject,
                RawValue = string.IsNullOrWhiteSpace(actualRaw) ? null : actualRaw.Trim(),
                NormalizedValue = NormalizeSelection(actualRaw),
                IsActualOutcome = true
            });
        }

        if (selections.Count == 0)
        {
            return 0;
        }

        dbContext.MigrationImportRaceSelections.RemoveRange(
            dbContext.MigrationImportRaceSelections.Where(x => x.ImportRunId == runId));
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.MigrationImportRaceSelections.AddRangeAsync(selections, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return selections.Count;
    }

    private static List<string> ResolveParticipants(string? headerPayload)
    {
        if (string.IsNullOrWhiteSpace(headerPayload))
        {
            return [];
        }

        var columns = ParseCsvLine(headerPayload);
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

    private static bool TryResolveRace(string label, ref string? currentRaceCode, out string raceCode, out string pickType)
    {
        raceCode = string.Empty;
        pickType = string.Empty;

        if (label.Length == 0)
        {
            return false;
        }

        var normalizedLabel = label.Trim();
        if (normalizedLabel.StartsWith("L-", StringComparison.OrdinalIgnoreCase))
        {
            normalizedLabel = normalizedLabel[2..];
        }

        if (normalizedLabel.Equals("DNF", StringComparison.OrdinalIgnoreCase) ||
            normalizedLabel.Contains("HUMBUG", StringComparison.OrdinalIgnoreCase))
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
            var genericRaceMatch = GenericRacePrefixRegex().Match(normalizedLabel);
            if (!genericRaceMatch.Success)
            {
                return false;
            }

            raceCode = genericRaceMatch.Groups[1].Value.ToUpperInvariant();
            pickType = "DNF";
            currentRaceCode = raceCode;
            return true;
        }

        raceCode = match.Groups[1].Value.ToUpperInvariant();
        pickType = match.Groups[2].Value.ToUpperInvariant();
        currentRaceCode = raceCode;
        return true;
    }

    private static string? NormalizeSelection(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var normalized = rawValue.Trim();
        if (normalized.Equals("NONE", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("NOT", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return normalized;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
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

    [GeneratedRegex("^([A-Za-z]{3})-(1|2|3|DNF)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RaceLabelRegex();

    [GeneratedRegex("^([A-Za-z]{3})-.+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GenericRacePrefixRegex();
}