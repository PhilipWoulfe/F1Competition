using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationScoreRecalculatorTests
{
    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPodiumAndDnfMatrixApplied_ComputesExpectedPoints()
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

        dbContext.MigrationImportRaceSelections.AddRange(
            // Actual outcomes for AUS.
            Selection(runId, 100, "AUS", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "AUS", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "AUS", "3", "ACTUAL", "LEC", isActual: true),
            Selection(runId, 103, "AUS", "DNF", "ACTUAL", "SAI DOO", isActual: true),

            // Philip: exact P1, top3 wrong-slot P2, podium miss P3, DNF match.
            Selection(runId, 10, "AUS", "1", "Philip", "VER"),
            Selection(runId, 11, "AUS", "2", "Philip", "LEC"),
            Selection(runId, 12, "AUS", "3", "Philip", "HAM"),
            Selection(runId, 13, "AUS", "DNF", "Philip", "DOO"),

            // Andy: blank DNF with actual DNFs should score 0.
            Selection(runId, 20, "AUS", "DNF", "Andy", null));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(5, result.ScoredPickCount);
        Assert.Equal(20, result.TotalPoints);

        var scores = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync();

        Assert.Equal(5, scores.Count);

        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "1"), 10, "PODIUM_EXACT");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "2"), 5, "PODIUM_TOP3_WRONG_SLOT");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "3"), 0, "PODIUM_MISS");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "DNF"), 5, "DNF_MATCH");
        AssertScore(scores.Single(x => x.Subject == "Andy" && x.PickType == "DNF"), 0, "DNF_BLANK_HAS_ACTUAL");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenNoActualDnfs_BlankDnfScoresFive()
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

        dbContext.MigrationImportRaceSelections.AddRange(
            Selection(runId, 100, "JPN", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "JPN", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "JPN", "3", "ACTUAL", "LEC", isActual: true),
            Selection(runId, 103, "JPN", "DNF", "ACTUAL", null, isActual: true),
            Selection(runId, 10, "JPN", "DNF", "Philip", null));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(1, result.ScoredPickCount);
        Assert.Equal(5, result.TotalPoints);

        var dnfScore = await dbContext.MigrationImportCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip" && x.PickType == "DNF");

        AssertScore(dnfScore, 5, "DNF_BLANK_NO_ACTUAL");
    }

    private static MigrationImportRaceSelectionEntity Selection(
        Guid runId,
        int rowNumber,
        string raceCode,
        string pickType,
        string subject,
        string? normalizedValue,
        bool isActual = false)
    {
        return new MigrationImportRaceSelectionEntity
        {
            ImportRunId = runId,
            RowNumber = rowNumber,
            RaceCode = raceCode,
            PickType = pickType,
            Subject = subject,
            RawValue = normalizedValue,
            NormalizedValue = normalizedValue,
            IsActualOutcome = isActual
        };
    }

    private static void AssertScore(MigrationImportCalculatedScoreEntity actual, int points, string reasonCode)
    {
        Assert.Equal(points, actual.Points);
        Assert.Equal(reasonCode, actual.ReasonCode);
        Assert.True(actual.Points >= 0);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m6-score-{Guid.NewGuid():N}")
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
