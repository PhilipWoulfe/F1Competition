using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public static class MigrationPhil2025CsvContractPolicy
{
    public const string SourceFileName = "PhilMigratedSelectionsAndScores.csv";

    public const int ParticipantStartColumnIndex = 1; // Column B
    public const int ParticipantEndColumnIndex = 10; // Column K
    public const int ActualAnswerColumnIndex = 11; // Column L
    public const int PreseasonPointsPolicyRow = 2;
    public const int PreseasonPointsPolicyColumnIndex = 12; // Column M (M2)

    public static readonly string[] ParticipantColumns =
    [
        "Philip",
        "New Sexy Ayrton",
        "Andy",
        "Claire",
        "Dave",
        "Kevin",
        "Pious",
        "Shane",
        "Veronica",
        "BinGPT"
    ];

    public const int HeaderRow = 1;
    public const int PreseasonQuestionStartRow = 2;
    public const int PreseasonQuestionEndRow = 21;
    public const int PreseasonPointsStartRow = 22;
    public const int PreseasonPointsEndRow = 41;
    public const int RaceSelectionStartRow = 43;
    public const int RaceSelectionEndRow = 138;
    public const int RacePointsStartRow = 140;
    public const int RacePointsEndRow = 235;

    private const string ContractReasonPrefix = "Phil 2025 CSV contract:";

    public static bool AppliesTo(string sourceFilePath)
    {
        return sourceFilePath.EndsWith(SourceFileName, StringComparison.OrdinalIgnoreCase);
    }

    public static StagedImportRow Apply(StagedImportRow row)
    {
        if (row.RowNumber == HeaderRow)
        {
            if (row.SectionType == MigrationImportSectionTypes.Header)
            {
                return row;
            }

            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason = BuildValidationReason(row.RowNumber, "must be a header row.")
            };
        }

        if (row.RowNumber >= PreseasonQuestionStartRow && row.RowNumber <= PreseasonQuestionEndRow)
        {
            if (row.SectionType == MigrationImportSectionTypes.Blank)
            {
                return row;
            }

            if (row.SectionType == MigrationImportSectionTypes.SeasonQuestionPrediction)
            {
                return row with
                {
                    ClassificationReason =
                        $"{ContractReasonPrefix} row {row.RowNumber} is within preseason question window {PreseasonQuestionStartRow}-{PreseasonQuestionEndRow}."
                };
            }

            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason = BuildValidationReason(
                    row.RowNumber,
                    $"must be a preseason question row ({PreseasonQuestionStartRow}-{PreseasonQuestionEndRow}) but was classified as {row.SectionType}.")
            };
        }

        if (row.RowNumber >= PreseasonPointsStartRow && row.RowNumber <= PreseasonPointsEndRow)
        {
            if (row.SectionType == MigrationImportSectionTypes.Blank)
            {
                return row;
            }

            if (row.SectionType == MigrationImportSectionTypes.SeasonQuestionPoints ||
                row.SectionType == MigrationImportSectionTypes.RacePoints)
            {
                return row with
                {
                    SectionType = MigrationImportSectionTypes.SeasonQuestionPoints,
                    ClassificationReason =
                        $"{ContractReasonPrefix} row {row.RowNumber} is within preseason tally window {PreseasonPointsStartRow}-{PreseasonPointsEndRow} and excluded from race scoring."
                };
            }

            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason = BuildValidationReason(
                    row.RowNumber,
                    $"must be a preseason points row ({PreseasonPointsStartRow}-{PreseasonPointsEndRow}) but was classified as {row.SectionType}.")
            };
        }

        if (row.RowNumber >= RaceSelectionStartRow && row.RowNumber <= RaceSelectionEndRow)
        {
            if (row.SectionType == MigrationImportSectionTypes.Blank ||
                row.SectionType == MigrationImportSectionTypes.RacePick)
            {
                return row;
            }

            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason = BuildValidationReason(
                    row.RowNumber,
                    $"must contain race selections within rows {RaceSelectionStartRow}-{RaceSelectionEndRow}.")
            };
        }

        if (row.RowNumber >= RacePointsStartRow && row.RowNumber <= RacePointsEndRow)
        {
            if (row.SectionType == MigrationImportSectionTypes.Blank ||
                row.SectionType == MigrationImportSectionTypes.RacePoints)
            {
                return row;
            }

            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason = BuildValidationReason(
                    row.RowNumber,
                    $"must contain race points within rows {RacePointsStartRow}-{RacePointsEndRow}.")
            };
        }

        if (row.SectionType == MigrationImportSectionTypes.RacePick)
        {
            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason = BuildValidationReason(
                    row.RowNumber,
                    $"race selections are only allowed on rows {RaceSelectionStartRow}-{RaceSelectionEndRow}.")
            };
        }

        if (row.SectionType == MigrationImportSectionTypes.RacePoints)
        {
            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason = BuildValidationReason(
                    row.RowNumber,
                    $"race points are only allowed on rows {RacePointsStartRow}-{RacePointsEndRow}.")
            };
        }

        return row;
    }

    private static string BuildValidationReason(int rowNumber, string reason)
    {
        return $"{ContractReasonPrefix} row {rowNumber} {reason}";
    }
}
