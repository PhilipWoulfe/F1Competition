using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Services;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationPhil2025CsvContractPolicyTests
{
    [Fact]
    public void ContractMetadata_DefinesParticipantActualAndPolicyColumns()
    {
        Assert.Equal(1, MigrationPhil2025CsvContractPolicy.ParticipantStartColumnIndex);
        Assert.Equal(10, MigrationPhil2025CsvContractPolicy.ParticipantEndColumnIndex);
        Assert.Equal(11, MigrationPhil2025CsvContractPolicy.ActualAnswerColumnIndex);
        Assert.Equal(2, MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyRow);
        Assert.Equal(12, MigrationPhil2025CsvContractPolicy.PreseasonPointsPolicyColumnIndex);
        Assert.Equal(10, MigrationPhil2025CsvContractPolicy.ParticipantColumns.Length);
    }

    [Fact]
    public void Apply_WhenPreseasonPointsRowLooksLikeRacePoints_OverridesToSeasonQuestionPoints()
    {
        var input = new StagedImportRow(
            RowNumber: 22,
            SectionType: MigrationImportSectionTypes.RacePoints,
            RawPayload: "AUS-1,20");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.SeasonQuestionPoints, result.SectionType);
        Assert.Contains("row 22", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preseason tally window", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WhenPreseasonQuestionRowIsMalformed_MarksUnclassifiedWithRowNumber()
    {
        var input = new StagedImportRow(
            RowNumber: 2,
            SectionType: MigrationImportSectionTypes.RacePoints,
            RawPayload: "AUS-1,20");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.Unclassified, result.SectionType);
        Assert.Contains("row 2", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must be a preseason question row", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WhenPreseasonPointsRowIsMalformed_MarksUnclassifiedWithRowNumber()
    {
        var input = new StagedImportRow(
            RowNumber: 22,
            SectionType: MigrationImportSectionTypes.RacePick,
            RawPayload: "AUS-1,VER");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.Unclassified, result.SectionType);
        Assert.Contains("row 22", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must be a preseason points row", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WhenPreseasonWindowRowIsBlank_LeavesBlankSection()
    {
        var input = new StagedImportRow(
            RowNumber: 10,
            SectionType: MigrationImportSectionTypes.Blank,
            RawPayload: ",,");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.Blank, result.SectionType);
        Assert.Null(result.ClassificationReason);
    }

    [Fact]
    public void Apply_WhenRaceSelectionWindowContainsWrongSection_MarksUnclassified()
    {
        var input = new StagedImportRow(
            RowNumber: 43,
            SectionType: MigrationImportSectionTypes.SeasonQuestionPrediction,
            RawPayload: "AUS-1,VER");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.Unclassified, result.SectionType);
        Assert.Contains("row 43", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must contain race selections", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WhenRacePickOutsideRaceSelectionWindow_MarksUnclassified()
    {
        var input = new StagedImportRow(
            RowNumber: 236,
            SectionType: MigrationImportSectionTypes.RacePick,
            RawPayload: "AUS-1,VER");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.Unclassified, result.SectionType);
        Assert.Contains("row 236", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only allowed on rows 43-138", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_WhenRacePointsOutsideRacePointsWindow_MarksUnclassified()
    {
        var input = new StagedImportRow(
            RowNumber: 42,
            SectionType: MigrationImportSectionTypes.RacePoints,
            RawPayload: "AUS-1,20");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.Unclassified, result.SectionType);
        Assert.Contains("row 42", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only allowed on rows 140-235", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppliesTo_WhenPhilSourceFileName_ReturnsTrue()
    {
        Assert.True(MigrationPhil2025CsvContractPolicy.AppliesTo("/tmp/PhilMigratedSelectionsAndScores.csv"));
    }

    [Fact]
    public void AppliesTo_WhenOtherSourceFileName_ReturnsFalse()
    {
        Assert.False(MigrationPhil2025CsvContractPolicy.AppliesTo("/tmp/other.csv"));
    }
}
