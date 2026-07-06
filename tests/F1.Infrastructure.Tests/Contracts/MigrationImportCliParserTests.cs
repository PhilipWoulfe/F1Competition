using F1.DataSyncWorker.Options;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationImportCliParserTests
{
    [Fact]
    public void ParseToConfiguration_WhenNoMigrationArgumentsProvided_ReturnsEmptyConfiguration()
    {
        var result = MigrationImportCliParser.ParseToConfiguration(["--verbosity", "minimal"]);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseToConfiguration_WhenArgumentsProvided_MapsToExpectedConfigurationKeys()
    {
        var result = MigrationImportCliParser.ParseToConfiguration(
        [
            "--source-file-path",
            "data/imports/phil-2025/custom.csv",
            "--season=2026",
            "--dry-run=false"
        ]);

        Assert.Equal("true", result["MigrationImport:Enabled"]);
        Assert.Equal("data/imports/phil-2025/custom.csv", result["MigrationImport:SourceFilePath"]);
        Assert.Equal("2026", result["MigrationImport:Season"]);
        Assert.Equal("false", result["MigrationImport:DryRun"]);
    }

    [Fact]
    public void ParseToConfiguration_WhenWriteModeEnabled_ForcesDryRunFalse()
    {
        var result = MigrationImportCliParser.ParseToConfiguration(
        [
            "--migration-import",
            "--dry-run=true",
            "--write-mode"
        ]);

        Assert.Equal("true", result["MigrationImport:Enabled"]);
        Assert.Equal("false", result["MigrationImport:DryRun"]);
    }

    [Fact]
    public void ParseToConfiguration_WhenMigrationImportExplicitlyDisabled_RespectsDisabledValue()
    {
        var result = MigrationImportCliParser.ParseToConfiguration(
        [
            "--migration-import=false",
            "--source=data/imports/phil-2025/custom.csv"
        ]);

        Assert.Equal("false", result["MigrationImport:Enabled"]);
        Assert.Equal("data/imports/phil-2025/custom.csv", result["MigrationImport:SourceFilePath"]);
    }

    [Fact]
    public void ParseToConfiguration_WhenSeasonIsNotInteger_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MigrationImportCliParser.ParseToConfiguration(["--season", "twenty-twenty-five"]));

        Assert.Contains("--season expects an integer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseToConfiguration_WhenSourcePathMissingValue_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MigrationImportCliParser.ParseToConfiguration(["--source-file-path"]));

        Assert.Contains("--source-file-path requires a value", ex.Message, StringComparison.Ordinal);
    }
}
