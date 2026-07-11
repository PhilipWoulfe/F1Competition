using F1.DataSyncWorker.Services;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class Dave2025SourcePackageContractTests : IDisposable
{
    private readonly string _tempDirectory;

    public Dave2025SourcePackageContractTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"dave2025-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Validate_WhenRequiredFilesPresent_ReturnsValid()
    {
        CreateFile(Dave2025SourcePackageContract.RacesFile, "header");
        CreateFile(Dave2025SourcePackageContract.BonusFile, "header");
        CreateFile(Dave2025SourcePackageContract.BonusAnswersFile, "header");
        CreateFile(Dave2025SourcePackageContract.SideBetsFile, "header");
        CreateFile(Dave2025SourcePackageContract.LeaderboardFile, "header");

        var result = Dave2025SourcePackageContract.Validate(_tempDirectory);

        Assert.True(result.AppliesContract);
        Assert.True(result.IsValid);
        Assert.Empty(result.MissingFiles);
    }

    [Fact]
    public void Validate_WhenRequiredFileMissing_ReturnsInvalidWithMissingFile()
    {
        CreateFile(Dave2025SourcePackageContract.RacesFile, "header");
        CreateFile(Dave2025SourcePackageContract.BonusFile, "header");
        CreateFile(Dave2025SourcePackageContract.BonusAnswersFile, "header");
        CreateFile(Dave2025SourcePackageContract.SideBetsFile, "header");

        var result = Dave2025SourcePackageContract.Validate(_tempDirectory);

        Assert.True(result.AppliesContract);
        Assert.False(result.IsValid);
        Assert.Contains(Dave2025SourcePackageContract.LeaderboardFile, result.MissingFiles, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComputeManifestChecksumAsync_WhenFileContentChanges_ChangesChecksum()
    {
        CreateFile(Dave2025SourcePackageContract.RacesFile, "one");
        CreateFile(Dave2025SourcePackageContract.BonusFile, "one");

        var first = await Dave2025SourcePackageContract.ComputeManifestChecksumAsync(_tempDirectory, CancellationToken.None);

        CreateFile(Dave2025SourcePackageContract.BonusFile, "two");
        var second = await Dave2025SourcePackageContract.ComputeManifestChecksumAsync(_tempDirectory, CancellationToken.None);

        Assert.NotEqual(first, second);
    }

    private void CreateFile(string fileName, string contents)
    {
        var path = Path.Combine(_tempDirectory, fileName);
        File.WriteAllText(path, contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}