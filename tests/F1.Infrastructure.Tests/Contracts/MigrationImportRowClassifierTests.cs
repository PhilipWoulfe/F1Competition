using F1.DataSyncWorker.Services;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationImportRowClassifierTests
{
    private readonly MigrationImportRowClassifier _classifier = new();

    [Theory]
    [InlineData("Question,Philip,Andy", "Header")]
    [InlineData("The WDC will drive for the WCC winning team?,Y,N", "SeasonQuestionPrediction")]
    [InlineData("The WDC will drive for the WCC winning team?,20,0", "SeasonQuestionPoints")]
    [InlineData("AUS-1,NOR,PIA", "RacePick")]
    [InlineData("AUS-1,10,5", "RacePoints")]
    [InlineData("COTA-1,VER,NOR", "RacePick")]
    [InlineData("COTA-1,10,5", "RacePoints")]
    [InlineData("DNF,NOR,PIA", "RacePick")]
    [InlineData("BAK-UP-BAK-UP-WHAT-YOU-GONNA-DO-NOW?,COL,LAW", "RacePick")]
    [InlineData("Result,590,550", "TotalsMeta")]
    [InlineData(",,,", "Blank")]
    public void Classify_WhenInputMatchesKnownPattern_ReturnsExpectedSection(string rawLine, string expectedSection)
    {
        var result = _classifier.Classify(1, rawLine);

        Assert.Equal(expectedSection, result.SectionType);
    }

    [Fact]
    public void Classify_WhenBahHumbugLabelPresent_MapsToRacePickWithReason()
    {
        var result = _classifier.Classify(42, "BAH-HUMBUG,STR,NOR");

        Assert.Equal("RacePick", result.SectionType);
        Assert.Equal("Mapped special label to DNF pick type.", result.ClassificationReason);
    }

    [Fact]
    public void Classify_WhenBakLabelPresent_MapsToRacePickWithReason()
    {
        var result = _classifier.Classify(43, "BAK-UP-BAK-UP-WHAT-YOU-GONNA-DO-NOW?,COL,LAW");

        Assert.Equal("RacePick", result.SectionType);
        Assert.Equal("Mapped special label to DNF pick type.", result.ClassificationReason);
    }

    [Fact]
    public void Classify_WhenRowCannotBeClassified_ReturnsReason()
    {
        var result = _classifier.Classify(8, "@@@,###,%%%");

        Assert.Equal("Unclassified", result.SectionType);
        Assert.False(string.IsNullOrWhiteSpace(result.ClassificationReason));
    }

    [Fact]
    public void Classify_WhenCalledRepeatedlyWithSameInput_IsDeterministic()
    {
        const string rawLine = "The WDC will drive for the WCC winning team?,Y,N";

        var first = _classifier.Classify(5, rawLine);
        var second = _classifier.Classify(5, rawLine);

        Assert.Equal(first, second);
    }
}