using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileAndPersistAsync_ProducesDeterministicPickDiffOrdering_FromGoldenFile()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "test.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportLegacyPickScores.AddRange(
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 3, RaceCode = "AUS", PickType = "DNF", Subject = "Philip", LegacyPoints = 5 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 4, RaceCode = "AUS", PickType = "1", Subject = "Andy", LegacyPoints = 5 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 5, RaceCode = "AUS", PickType = "2", Subject = "Andy", LegacyPoints = 5 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 6, RaceCode = "AUS", PickType = "DNF", Subject = "Andy", LegacyPoints = 5 });

        dbContext.MigrationImportCalculatedScores.AddRange(
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 3, RaceCode = "AUS", PickType = "DNF", Subject = "Philip", Points = 0, ReasonCode = "DNF_MISS" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 4, RaceCode = "AUS", PickType = "1", Subject = "Andy", Points = 5, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 5, RaceCode = "AUS", PickType = "2", Subject = "Andy", Points = 10, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 6, RaceCode = "AUS", PickType = "DNF", Subject = "Andy", Points = 5, ReasonCode = "DNF_MATCH" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 7, RaceCode = "BHR", PickType = "1", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" });

        await dbContext.SaveChangesAsync();

        var service = new MigrationReconciliationService(new TestDbContextFactory(options));
        var result = await service.ReconcileAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(7, result.PickDiffCount);
        Assert.Equal(3, result.RaceDiffCount);
        Assert.Equal(2, result.ParticipantSummaryCount);
        Assert.Equal(3, result.ReasonSummaryCount);
        Assert.Equal(5, result.TotalDelta);

        var pickDiffs = await dbContext.MigrationImportPickDiffs
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceCode)
            .ThenBy(x => x.Subject)
            .ThenBy(x => x.PickType)
            .ToListAsync();

        var actualLines = pickDiffs
            .Select(x => string.Join('|',
                x.RaceCode,
                x.Subject,
                x.PickType,
                x.ImportedPoints?.ToString() ?? string.Empty,
                x.CalculatedPoints?.ToString() ?? string.Empty,
                x.DeltaPoints,
                x.ReasonCode))
            .ToArray();

        var expectedLines = await File.ReadAllLinesAsync(GetGoldenFilePath("MigrationReconciliationExpectedOrder.txt"));
        Assert.Equal(expectedLines, actualLines);

        var nonZero = pickDiffs.Where(x => x.DeltaPoints != 0).ToList();
        Assert.NotEmpty(nonZero);
        Assert.All(nonZero, x => Assert.False(string.IsNullOrWhiteSpace(x.Explanation)));

        var philipAusRaceDiff = await dbContext.MigrationImportRaceDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "AUS" && x.Subject == "Philip");
        Assert.Contains("Contributors:", philipAusRaceDiff.Explanation);
        Assert.Contains("AUS-1 10->5 (-5)", philipAusRaceDiff.Explanation);
        Assert.Contains("AUS-DNF 5->0 (-5)", philipAusRaceDiff.Explanation);

        var andyAusRaceDiff = await dbContext.MigrationImportRaceDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "AUS" && x.Subject == "Andy");
        Assert.Contains("AUS-2 5->10 (5)", andyAusRaceDiff.Explanation);
    }

    [Fact]
    public async Task ReconcileAndPersistAsync_PersistsParticipantAndReasonSummaries()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "test.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportLegacyPickScores.AddRange(
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", LegacyPoints = 10 });

        dbContext.MigrationImportCalculatedScores.AddRange(
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" });

        await dbContext.SaveChangesAsync();

        var service = new MigrationReconciliationService(new TestDbContextFactory(options));
        await service.ReconcileAndPersistAsync(runId, CancellationToken.None);

        var participantSummary = await dbContext.MigrationImportParticipantDeltaSummaries
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip");

        Assert.Equal(20, participantSummary.ImportedTotalPoints);
        Assert.Equal(15, participantSummary.CalculatedTotalPoints);
        Assert.Equal(-5, participantSummary.NetDeltaPoints);
        Assert.Equal("PODIUM_RULE_VARIANCE", participantSummary.TopReasonCode);
        Assert.Equal(1, participantSummary.TopReasonCount);

        var reasonSummary = await dbContext.MigrationImportReasonCategorySummaries
            .SingleAsync(x => x.ImportRunId == runId && x.ReasonCode == "PODIUM_RULE_VARIANCE");

        Assert.Equal(1, reasonSummary.OccurrenceCount);
        Assert.Equal(-5, reasonSummary.TotalDeltaPoints);
    }

    private static string GetGoldenFilePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "F1.Infrastructure.Tests.csproj")))
            {
                return Path.Combine(directory.FullName, "Contracts", "Golden", fileName);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate F1.Infrastructure.Tests project root.");
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m8-reconciliation-{Guid.NewGuid():N}")
            .Options;
    }

    private sealed class TestDbContextFactory : IDbContextFactory<F1DbContext>
    {
        private readonly DbContextOptions<F1DbContext> _options;

        public TestDbContextFactory(DbContextOptions<F1DbContext> options)
        {
            _options = options;
        }

        public F1DbContext CreateDbContext()
        {
            return new F1DbContext(_options);
        }

        public ValueTask<F1DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(CreateDbContext());
        }
    }
}
