using F1.DataSyncWorker.Models;

namespace F1.DataSyncWorker.Services;

public static class MigrationPhil2025CsvContractPolicy
{
    public const string SourceFileName = "PhilMigratedSelectionsAndScores.csv";
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
                ClassificationReason = $"{ContractReasonPrefix} row 1 must be a header row."
            };
        }

        if (row.RowNumber >= PreseasonQuestionStartRow && row.RowNumber <= PreseasonQuestionEndRow)
        {
            if (row.SectionType == MigrationImportSectionTypes.Blank)
            {
                return row;
            }

            return row with
            {
                SectionType = MigrationImportSectionTypes.SeasonQuestionPrediction,
                ClassificationReason =
                    $"{ContractReasonPrefix} rows {PreseasonQuestionStartRow}-{PreseasonQuestionEndRow} are preseason questions and excluded from race scoring."
            };
        }

        if (row.RowNumber >= PreseasonPointsStartRow && row.RowNumber <= PreseasonPointsEndRow)
        {
            if (row.SectionType == MigrationImportSectionTypes.Blank)
            {
                return row;
            }

            return row with
            {
                SectionType = MigrationImportSectionTypes.SeasonQuestionPoints,
                ClassificationReason =
                    $"{ContractReasonPrefix} rows {PreseasonPointsStartRow}-{PreseasonPointsEndRow} are preseason point tallies and excluded from race scoring."
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
                ClassificationReason =
                    $"{ContractReasonPrefix} rows {RaceSelectionStartRow}-{RaceSelectionEndRow} must contain race selections."
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
                ClassificationReason =
                    $"{ContractReasonPrefix} rows {RacePointsStartRow}-{RacePointsEndRow} must contain race points."
            };
        }

        if (row.SectionType == MigrationImportSectionTypes.RacePick)
        {
            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason =
                    $"{ContractReasonPrefix} race selections are only allowed on rows {RaceSelectionStartRow}-{RaceSelectionEndRow}."
            };
        }

        if (row.SectionType == MigrationImportSectionTypes.RacePoints)
        {
            return row with
            {
                SectionType = MigrationImportSectionTypes.Unclassified,
                ClassificationReason =
                    $"{ContractReasonPrefix} race points are only allowed on rows {RacePointsStartRow}-{RacePointsEndRow}."
            };
        }

        return row;
    }
}
