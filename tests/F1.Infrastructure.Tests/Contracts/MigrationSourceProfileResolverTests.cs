using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Services;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationSourceProfileResolverTests : IDisposable
{
    private readonly string _tempDirectory;

    public MigrationSourceProfileResolverTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"migration-source-profile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Resolve_WhenPhilSourceFile_ReturnsPhil2025Profile()
    {
        var filePath = Path.Combine(_tempDirectory, MigrationPhil2025CsvContractPolicy.SourceFileName);
        File.WriteAllText(filePath, "Question,Philip");

        var profile = MigrationSourceProfileResolver.Resolve(filePath);

        Assert.Equal(MigrationSourceProfile.Phil2025Csv, profile);
    }

    [Fact]
    public void Resolve_WhenDavePackageDirectory_ReturnsDaveProfile()
    {
        File.WriteAllText(Path.Combine(_tempDirectory, Dave2025SourcePackageContract.RacesFile), "Name");
        File.WriteAllText(Path.Combine(_tempDirectory, Dave2025SourcePackageContract.BonusFile), "Question");
        File.WriteAllText(Path.Combine(_tempDirectory, Dave2025SourcePackageContract.BonusAnswersFile), "Question,Answer");
        File.WriteAllText(Path.Combine(_tempDirectory, Dave2025SourcePackageContract.LeaderboardFile), "Name,Total");

        var profile = MigrationSourceProfileResolver.Resolve(_tempDirectory);

        Assert.Equal(MigrationSourceProfile.Dave2025Package, profile);
    }

    [Fact]
    public void Resolve_WhenPathUnknown_ReturnsUnknown()
    {
        var profile = MigrationSourceProfileResolver.Resolve(Path.Combine(_tempDirectory, "unknown.csv"));
        Assert.Equal(MigrationSourceProfile.Unknown, profile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
