using F1.Core.Interfaces;
using F1.Core.Models;

namespace F1.Infrastructure.Tests.Contracts;

public abstract class RaceMetadataRepositoryContractTests
{
    // Returns a repository where no race documents exist.
    protected abstract IMetadataTestRepository CreateEmptyRepository();

    // Returns a repository that has a race document for raceId with the given metadata already stored.
    protected abstract IMetadataTestRepository CreateRepositoryWithMetadata(string raceId, RaceQuestionMetadata metadata);

    // Returns a repository that has a race document for raceId but no metadata on it yet.
    protected abstract IMetadataTestRepository CreateRepositoryWithRaceDocumentOnly(string raceId);

    [Fact]
    public async Task GetMetadataAsync_ReturnsNull_WhenNoMetadataExists()
    {
        var repo = CreateEmptyRepository().Repository;

        var result = await repo.GetMetadataAsync("no-such-race");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMetadataAsync_ReturnsMappedMetadata_WhenExists()
    {
        var raceId = "main-2026-1-test-grand-prix";
        var metadata = new RaceQuestionMetadata
        {
            RaceId = raceId,
            H2HQuestion = "Who finishes higher: Norris or Leclerc?",
            H2HLeftDriverId = "NOR",
            H2HRightDriverId = "LEC",
            H2HPoints = 5,
            BonusQuestion = "How many safety car laps?",
            IsPublished = true,
            UpdatedAtUtc = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc)
        };

        var repo = CreateRepositoryWithMetadata(raceId, metadata).Repository;

        var result = await repo.GetMetadataAsync(raceId);

        Assert.NotNull(result);
        Assert.Equal(raceId, result!.RaceId);
        Assert.Equal("Who finishes higher: Norris or Leclerc?", result.H2HQuestion);
        Assert.Equal("NOR", result.H2HLeftDriverId);
        Assert.Equal("LEC", result.H2HRightDriverId);
        Assert.Equal(5, result.H2HPoints);
        Assert.Equal("How many safety car laps?", result.BonusQuestion);
        Assert.True(result.IsPublished);
    }

    [Fact]
    public async Task UpsertMetadataAsync_UpdatesExistingMetadata()
    {
        var raceId = "main-2026-2-test-grand-prix";
        var original = new RaceQuestionMetadata
        {
            RaceId = raceId,
            H2HQuestion = "Old H2H",
            H2HLeftDriverId = "HAM",
            H2HRightDriverId = "VER",
            H2HPoints = 3,
            BonusQuestion = "Old bonus",
            IsPublished = false,
            UpdatedAtUtc = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var fixture = CreateRepositoryWithMetadata(raceId, original);

        var updated = new RaceQuestionMetadata
        {
            RaceId = raceId,
            H2HQuestion = "New H2H",
            H2HLeftDriverId = "NOR",
            H2HRightDriverId = "PIA",
            H2HPoints = 7,
            BonusQuestion = "New bonus",
            IsPublished = true,
            UpdatedAtUtc = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc)
        };

        var result = await fixture.Repository.UpsertMetadataAsync(raceId, updated);

        Assert.NotNull(result);
        Assert.Equal("New H2H", result.H2HQuestion);
        Assert.Equal("NOR", result.H2HLeftDriverId);
        Assert.Equal("PIA", result.H2HRightDriverId);
        Assert.Equal(7, result.H2HPoints);
        Assert.Equal("New bonus", result.BonusQuestion);
        Assert.True(result.IsPublished);
    }

    [Fact]
    public async Task UpsertMetadataAsync_ThrowsKeyNotFoundException_WhenRaceDocumentMissing()
    {
        var repo = CreateEmptyRepository().Repository;

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            repo.UpsertMetadataAsync("no-such-race", new RaceQuestionMetadata
            {
                H2HQuestion = "irrelevant",
                H2HLeftDriverId = "HAM",
                H2HRightDriverId = "VER",
                H2HPoints = 5,
                BonusQuestion = "irrelevant",
                IsPublished = false
            }));
    }
}

// Allows implementations to expose additional per-fixture context (e.g. seeded document Id)
// without changing the IRaceMetadataRepository interface.
public interface IMetadataTestRepository
{
    IRaceMetadataRepository Repository { get; }
}
