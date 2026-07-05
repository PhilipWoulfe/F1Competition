using System.Text;
using System.Text.RegularExpressions;
using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public sealed partial class MigrationImportRowClassifier : IMigrationImportRowClassifier
{
    private const string SectionBlank = "Blank";
    private const string SectionHeader = "Header";
    private const string SectionSeasonQuestionPrediction = "SeasonQuestionPrediction";
    private const string SectionSeasonQuestionPoints = "SeasonQuestionPoints";
    private const string SectionRacePick = "RacePick";
    private const string SectionRacePoints = "RacePoints";
    private const string SectionTotalsMeta = "TotalsMeta";
    private const string SectionUnclassified = "Unclassified";

    public StagedImportRow Classify(int rowNumber, string rawLine)
    {
        var columns = ParseCsvLine(rawLine);
        if (columns.Count == 0)
        {
            return new StagedImportRow(rowNumber, SectionBlank, rawLine);
        }

        var label = columns[0].Trim();
        var values = columns.Skip(1).Select(x => x.Trim()).ToArray();

        if (IsBlankRow(columns))
        {
            return new StagedImportRow(rowNumber, SectionBlank, rawLine);
        }

        if (label.Equals("Question", StringComparison.OrdinalIgnoreCase))
        {
            return new StagedImportRow(rowNumber, SectionHeader, rawLine);
        }

        if (label.Equals("Result", StringComparison.OrdinalIgnoreCase))
        {
            return new StagedImportRow(rowNumber, SectionTotalsMeta, rawLine);
        }

        if (label.Length == 0)
        {
            var section = LooksNumericRow(values) ? SectionTotalsMeta : SectionUnclassified;
            var reason = section == SectionUnclassified
                ? "First column is blank and row could not be matched to a numeric totals/meta pattern."
                : null;
            return new StagedImportRow(rowNumber, section, rawLine, reason);
        }

        if (IsRaceRowLabel(label, out var normalizedAsDnf))
        {
            var section = LooksNumericRow(values) ? SectionRacePoints : SectionRacePick;
            var reason = normalizedAsDnf ? "Mapped BAH-HUMBUG label to DNF pick type." : null;
            return new StagedImportRow(rowNumber, section, rawLine, reason);
        }

        if (!ContainsAlphaNumeric(label))
        {
            return new StagedImportRow(
                rowNumber,
                SectionUnclassified,
                rawLine,
                "Label does not contain alphanumeric content and cannot be classified.");
        }

        if (LooksNumericRow(values))
        {
            return new StagedImportRow(rowNumber, SectionSeasonQuestionPoints, rawLine);
        }

        if (LooksPredictionRow(values))
        {
            return new StagedImportRow(rowNumber, SectionSeasonQuestionPrediction, rawLine);
        }

        return new StagedImportRow(
            rowNumber,
            SectionUnclassified,
            rawLine,
            "Unable to classify row from label and value pattern.");
    }

    private static bool IsRaceRowLabel(string label, out bool normalizedAsDnf)
    {
        normalizedAsDnf = false;

        if (RaceRowRegex().IsMatch(label))
        {
            return true;
        }

        if (label.Contains("HUMBUG", StringComparison.OrdinalIgnoreCase))
        {
            normalizedAsDnf = true;
            return true;
        }

        return false;
    }

    private static bool LooksPredictionRow(IReadOnlyCollection<string> values)
    {
        var nonBlank = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (nonBlank.Length == 0)
        {
            return false;
        }

        var alphaCount = nonBlank.Count(value => !int.TryParse(value, out _));
        return alphaCount >= Math.Ceiling(nonBlank.Length * 0.6);
    }

    private static bool ContainsAlphaNumeric(string value)
    {
        return value.Any(char.IsLetterOrDigit);
    }

    private static bool LooksNumericRow(IReadOnlyCollection<string> values)
    {
        var nonBlank = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (nonBlank.Length == 0)
        {
            return false;
        }

        var numericCount = nonBlank.Count(value => int.TryParse(value, out _));
        return numericCount >= Math.Ceiling(nonBlank.Length * 0.8);
    }

    private static bool IsBlankRow(IReadOnlyCollection<string> values)
    {
        return values.All(string.IsNullOrWhiteSpace);
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

    [GeneratedRegex("^[A-Za-z]{3}-(1|2|3|DNF)$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RaceRowRegex();
}