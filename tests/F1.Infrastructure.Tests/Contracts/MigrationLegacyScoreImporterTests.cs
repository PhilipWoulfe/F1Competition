using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationLegacyScoreImporterTests
{
    [Fact]
    public async Task ImportAndPersistAsync_WhenRacePointsAndTotalsPresent_PersistsLegacyScoresAndSeparateTotals()
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

        dbContext.MigrationImportRawRows.AddRange(
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,Andy,BINGPT,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePoints", RawPayload = "AUS-1,10,5,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePoints", RawPayload = "AUS-2,5,10,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 4, SectionType = "RacePoints", RawPayload = "DNF,5,,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 5, SectionType = "TotalsMeta", RawPayload = "Result,590,550,410" });

        dbContext.MigrationImportCalculatedScores.AddRange(
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "1", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 3, RaceCode = "AUS", PickType = "2", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 4, RaceCode = "AUS", PickType = "DNF", Subject = "Philip", Points = 5, ReasonCode = "DNF_MATCH" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "1", Subject = "Andy", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        var result = await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(8, result.LegacyPickScoreCount);
        Assert.Equal(3, result.ImportedTotalCount);
        Assert.Equal(2, result.CalculatedTotalCount);

        var legacyScores = await dbContext.MigrationImportLegacyPickScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(8, legacyScores.Count);
        Assert.Contains(legacyScores, x => x.RaceCode == "AUS" && x.PickType == "DNF" && x.Subject == "Philip" && x.LegacyPoints == 5);
        Assert.Contains(legacyScores, x => x.RaceCode == "AUS" && x.PickType == "1" && x.Subject == "Andy" && x.LegacyPoints == 5);

        var importedTotals = await dbContext.MigrationImportImportedTotals
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(3, importedTotals.Count);
        Assert.Equal(590, importedTotals.Single(x => x.Subject == "Philip").ImportedTotalPoints);

        var calculatedTotals = await dbContext.MigrationImportCalculatedTotals
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(2, calculatedTotals.Count);
        Assert.Equal(20, calculatedTotals.Single(x => x.Subject == "Philip").CalculatedTotalPoints);
        Assert.Equal(5, calculatedTotals.Single(x => x.Subject == "Andy").CalculatedTotalPoints);

        // Imported and calculated totals are intentionally persisted separately.
        Assert.NotEqual(
            importedTotals.Single(x => x.Subject == "Philip").ImportedTotalPoints,
            calculatedTotals.Single(x => x.Subject == "Philip").CalculatedTotalPoints);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenTotalsContainNonNumericValue_PersistsRawAndNullImportedPoints()
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

        dbContext.MigrationImportRawRows.AddRange(
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "TotalsMeta", RawPayload = "Result,N/A" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        var result = await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(0, result.LegacyPickScoreCount);
        Assert.Equal(1, result.ImportedTotalCount);
        Assert.Equal(0, result.CalculatedTotalCount);

        var total = await dbContext.MigrationImportImportedTotals
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip");

        Assert.Equal("N/A", total.RawTotal);
        Assert.Null(total.ImportedTotalPoints);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m7-legacy-{Guid.NewGuid():N}")
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
