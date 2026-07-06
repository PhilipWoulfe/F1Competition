using System.Text;
using System.Text.RegularExpressions;
using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationRaceSelectionParser : IMigrationRaceSelectionParser
{
    private const string SectionTypeRacePick = "RacePick";
    private const string SectionTypeHeader = "Header";
    private const string ActualSubject = "ACTUAL";
    private static readonly Dictionary<string, string> RaceCodeAliasDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AUS"] = "albert_park",
        ["AUSTRALIA"] = "albert_park",
        ["ALBERT PARK"] = "albert_park",
        ["CHN"] = "shanghai",
        ["CHINA"] = "shanghai",
        ["SHANGHAI"] = "shanghai",
        ["JPN"] = "suzuka",
        ["JAPAN"] = "suzuka",
        ["SUZUKA"] = "suzuka",
        ["BAH"] = "bahrain",
        ["BAHRAIN"] = "bahrain",
        ["SAR"] = "jeddah",
        ["SAUDI"] = "jeddah",
        ["JEDDAH"] = "jeddah",
        ["MIA"] = "miami",
        ["MIAMI"] = "miami",
        ["IMO"] = "imola",
        ["IMOLA"] = "imola",
        ["MON"] = "monaco",
        ["MONACO"] = "monaco",
        ["MONZA"] = "monza",
        ["MNZ"] = "monza",
        ["ITA"] = "monza",
        ["ITALY"] = "monza",
        ["BAR"] = "catalunya",
        ["ESP"] = "catalunya",
        ["SPAIN"] = "catalunya",
        ["CAN"] = "villeneuve",
        ["CANADA"] = "villeneuve",
        ["AUSTRIA"] = "red_bull_ring",
        ["AUT"] = "red_bull_ring",
        ["RED BULL RING"] = "red_bull_ring",
        ["GBR"] = "silverstone",
        ["BRITAIN"] = "silverstone",
        ["SILVERSTONE"] = "silverstone",
        ["SPA"] = "spa",
        ["BEL"] = "spa",
        ["BELGIUM"] = "spa",
        ["HUN"] = "hungaroring",
        ["HUNGARY"] = "hungaroring",
        ["NED"] = "zandvoort",
        ["NETHERLANDS"] = "zandvoort",
        ["BAK"] = "baku",
        ["AZE"] = "baku",
        ["AZERBAIJAN"] = "baku",
        ["SIN"] = "marina_bay",
        ["SINGAPORE"] = "marina_bay",
        ["COTA"] = "americas",
        ["USA"] = "americas",
        ["UNITED STATES"] = "americas",
        ["MEX"] = "rodriguez",
        ["MEXICO"] = "rodriguez",
        ["BRA"] = "interlagos",
        ["BRAZIL"] = "interlagos",
        ["LAS"] = "las_vegas",
        ["VEGAS"] = "las_vegas",
        ["QAT"] = "losail",
        ["QATAR"] = "losail",
        ["ABD"] = "yas_marina",
        ["ABU DHABI"] = "yas_marina"
    };
    private static readonly Dictionary<string, string?> TokenAliasDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MAX"] = "VER",
        ["HULK"] = "HUL",
        ["BEAR MAN"] = "BEA",
        ["BEAR"] = "BEA",
        ["BORT"] = "BOR",
        ["LEEC"] = "LEC",
        ["NONE"] = null,
        ["NOT"] = null
    };

    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;

    public MigrationRaceSelectionParser(IDbContextFactory<F1DbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<MigrationRaceSelectionParseResult> ParseAndPersistAsync(Guid runId, CancellationToken cancellationToken)
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
            return new MigrationRaceSelectionParseResult(SelectionCount: 0, UnresolvedTokenCount: 0);
        }

        var raceRows = stagedRows.Where(x => string.Equals(x.SectionType, SectionTypeRacePick, StringComparison.Ordinal));
        var selections = new List<MigrationImportRaceSelectionEntity>();
        var unresolvedTokens = new List<MigrationImportUnresolvedTokenEntity>();
        string? currentRaceCode = null;
        var createdAtUtc = DateTime.UtcNow;

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
                var normalization = NormalizeSelection(rawValue, pickType);
                selections.Add(new MigrationImportRaceSelectionEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = raceCode,
                    PickType = pickType,
                    Subject = participants[index],
                    RawValue = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim(),
                    NormalizedValue = normalization.NormalizedValue,
                    IsActualOutcome = false
                });

                if (normalization.UnresolvedTokens.Count > 0)
                {
                    unresolvedTokens.AddRange(normalization.UnresolvedTokens.Select(unresolvedToken => new MigrationImportUnresolvedTokenEntity
                    {
                        ImportRunId = runId,
                        RowNumber = row.RowNumber,
                        RaceCode = raceCode,
                        PickType = pickType,
                        Subject = participants[index],
                        RawToken = unresolvedToken,
                        CreatedAtUtc = createdAtUtc
                    }));
                }
            }

            var actualRaw = columns.Skip(1 + participants.Count).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var actualNormalization = NormalizeSelection(actualRaw, pickType);
            selections.Add(new MigrationImportRaceSelectionEntity
            {
                ImportRunId = runId,
                RowNumber = row.RowNumber,
                RaceCode = raceCode,
                PickType = pickType,
                Subject = ActualSubject,
                RawValue = string.IsNullOrWhiteSpace(actualRaw) ? null : actualRaw.Trim(),
                NormalizedValue = actualNormalization.NormalizedValue,
                IsActualOutcome = true
            });

            if (actualNormalization.UnresolvedTokens.Count > 0)
            {
                unresolvedTokens.AddRange(actualNormalization.UnresolvedTokens.Select(unresolvedToken => new MigrationImportUnresolvedTokenEntity
                {
                    ImportRunId = runId,
                    RowNumber = row.RowNumber,
                    RaceCode = raceCode,
                    PickType = pickType,
                    Subject = ActualSubject,
                    RawToken = unresolvedToken,
                    CreatedAtUtc = createdAtUtc
                }));
            }
        }

        if (selections.Count == 0)
        {
            return new MigrationRaceSelectionParseResult(SelectionCount: 0, UnresolvedTokenCount: 0);
        }

        dbContext.MigrationImportRaceSelections.RemoveRange(
            dbContext.MigrationImportRaceSelections.Where(x => x.ImportRunId == runId));
        dbContext.MigrationImportUnresolvedTokens.RemoveRange(
            dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId));
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.MigrationImportRaceSelections.AddRangeAsync(selections, cancellationToken);
        if (unresolvedTokens.Count > 0)
        {
            await dbContext.MigrationImportUnresolvedTokens.AddRangeAsync(unresolvedTokens, cancellationToken);
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationRaceSelectionParseResult(
            SelectionCount: selections.Count,
            UnresolvedTokenCount: unresolvedTokens.Count);
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

        if (normalizedLabel.Contains("HUMBUG", StringComparison.OrdinalIgnoreCase))
        {
            var humbugMatch = GenericRacePrefixRegex().Match(normalizedLabel);
            if (!humbugMatch.Success)
            {
                return false;
            }

            raceCode = NormalizeRaceCode(humbugMatch.Groups[1].Value);
            pickType = "DNF";
            currentRaceCode = raceCode;
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

            raceCode = NormalizeRaceCode(genericRaceMatch.Groups[1].Value);
            pickType = "DNF";
            currentRaceCode = raceCode;
            return true;
        }

        raceCode = NormalizeRaceCode(match.Groups[1].Value);
        pickType = match.Groups[2].Value.ToUpperInvariant();
        currentRaceCode = raceCode;
        return true;
    }

    private static string NormalizeRaceCode(string raceToken)
    {
        var upper = raceToken.Trim().ToUpperInvariant();
        if (RaceCodeAliasDictionary.TryGetValue(upper, out var mapped))
        {
            return mapped;
        }

        return upper.Length <= 3 ? upper : upper[..3];
    }

    private static NormalizationResult NormalizeSelection(string? rawValue, string pickType)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new NormalizationResult(NormalizedValue: null, UnresolvedTokens: []);
        }

        var normalized = rawValue.Trim();
        var lookupToken = NormalizeTokenLookup(rawValue);

        if (lookupToken.Length == 0)
        {
            return new NormalizationResult(NormalizedValue: null, UnresolvedTokens: []);
        }

        // DNF and ACTUAL DNF values can be comma/space-separated token sets.
        if (string.Equals(pickType, "DNF", StringComparison.OrdinalIgnoreCase) && LooksLikeMultiTokenDnf(rawValue))
        {
            var resolvedTokens = new List<string>();
            var unresolvedTokens = new List<string>();

            foreach (var token in DnfTokenSplitRegex().Split(lookupToken).Where(token => token.Length > 0))
            {
                if (TokenAliasDictionary.TryGetValue(token, out var mappedToken))
                {
                    if (!string.IsNullOrWhiteSpace(mappedToken))
                    {
                        resolvedTokens.Add(mappedToken);
                    }

                    continue;
                }

                if (CanonicalTokenRegex().IsMatch(token))
                {
                    resolvedTokens.Add(token);
                    continue;
                }

                unresolvedTokens.Add(token);
            }

            var normalizedDnf = resolvedTokens.Count == 0 ? null : string.Join(" ", resolvedTokens);
            return new NormalizationResult(NormalizedValue: normalizedDnf, UnresolvedTokens: unresolvedTokens);
        }

        if (TokenAliasDictionary.TryGetValue(lookupToken, out var mappedSingleToken))
        {
            return new NormalizationResult(NormalizedValue: mappedSingleToken, UnresolvedTokens: []);
        }

        if (CanonicalTokenRegex().IsMatch(lookupToken))
        {
            return new NormalizationResult(NormalizedValue: lookupToken, UnresolvedTokens: []);
        }

        return new NormalizationResult(NormalizedValue: normalized, UnresolvedTokens: [normalized]);
    }

    private static bool LooksLikeMultiTokenDnf(string rawValue)
    {
        return rawValue.Contains(',') || DnfTokenSplitRegex().Split(rawValue.Trim()).Length > 1;
    }

    private static string NormalizeTokenLookup(string rawValue)
    {
        return MultiWhitespaceRegex().Replace(rawValue.Trim().ToUpperInvariant(), " ");
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

    [GeneratedRegex("^([A-Za-z]{3,})-(1|2|3|DNF)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RaceLabelRegex();

    [GeneratedRegex("^([A-Za-z]{3,})-.+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GenericRacePrefixRegex();

    [GeneratedRegex("^[A-Z]{3}$", RegexOptions.Compiled)]
    private static partial Regex CanonicalTokenRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex("[\\s,;/]+", RegexOptions.Compiled)]
    private static partial Regex DnfTokenSplitRegex();

    private readonly record struct NormalizationResult(string? NormalizedValue, IReadOnlyList<string> UnresolvedTokens);
}