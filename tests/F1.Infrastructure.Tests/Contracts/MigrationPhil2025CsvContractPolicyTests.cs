using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Services;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationPhil2025CsvContractPolicyTests
{
    [Fact]
    public void Apply_WhenPreseasonPointsRowLooksLikeRacePoints_OverridesToSeasonQuestionPoints()
    {
        var input = new StagedImportRow(
            RowNumber: 22,
            SectionType: MigrationImportSectionTypes.RacePoints,
            RawPayload: "AUS-1,20");

        var result = MigrationPhil2025CsvContractPolicy.Apply(input);

        Assert.Equal(MigrationImportSectionTypes.SeasonQuestionPoints, result.SectionType);
        Assert.Contains("excluded from race scoring", result.ClassificationReason, StringComparison.OrdinalIgnoreCase);
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
