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
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "albert_park", PickType = "1", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 3, RaceCode = "albert_park", PickType = "2", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 4, RaceCode = "albert_park", PickType = "DNF", Subject = "Philip", Points = 5, ReasonCode = "DNF_MATCH" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "albert_park", PickType = "1", Subject = "Andy", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" });

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
        Assert.Contains(legacyScores, x => x.RaceCode == "albert_park" && x.PickType == "DNF" && x.Subject == "Philip" && x.LegacyPoints == 5);
        Assert.Contains(legacyScores, x => x.RaceCode == "albert_park" && x.PickType == "1" && x.Subject == "Andy" && x.LegacyPoints == 5);

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

    [Fact]
    public async Task ImportAndPersistAsync_WhenLongRaceLabelsProvided_MapsMonzaAndAustriaToJolpicaCircuitIds()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePoints", RawPayload = "MONZA-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePoints", RawPayload = "AUSTRIA-2,5" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 4, SectionType = "TotalsMeta", RawPayload = "Result,15" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        var result = await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(2, result.LegacyPickScoreCount);

        var legacyScores = await dbContext.MigrationImportLegacyPickScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync();

        Assert.Equal("monza", legacyScores[0].RaceCode);
        Assert.Equal("red_bull_ring", legacyScores[1].RaceCode);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenMultiWordRaceLabelsProvided_MapsToExpectedCircuitIds()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePoints", RawPayload = "ABU DHABI-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePoints", RawPayload = "UNITED STATES-2,5" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 4, SectionType = "TotalsMeta", RawPayload = "Result,15" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        var result = await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(2, result.LegacyPickScoreCount);

        var legacyScores = await dbContext.MigrationImportLegacyPickScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync();

        Assert.Equal("yas_marina", legacyScores[0].RaceCode);
        Assert.Equal("americas", legacyScores[1].RaceCode);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenRoundMappingsExist_UsesMappedCircuitIdsForAllLegacyRaceCodes()
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

        dbContext.MigrationImportRaceRoundMappings.AddRange(
            new MigrationImportRaceRoundMappingEntity
            {
                ImportRunId = runId,
                RaceSequence = 1,
                SourceRowNumber = 2,
                SourceRaceCode = "AUS",
                Season = 2025,
                Round = 1,
                MappedCircuitId = "albert_park",
                MappedRaceName = "Australian Grand Prix"
            },
            new MigrationImportRaceRoundMappingEntity
            {
                ImportRunId = runId,
                RaceSequence = 2,
                SourceRowNumber = 6,
                SourceRaceCode = "CHN",
                Season = 2025,
                Round = 2,
                MappedCircuitId = "shanghai",
                MappedRaceName = "Chinese Grand Prix"
            });

        dbContext.MigrationImportRawRows.AddRange(
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePoints", RawPayload = "AUS-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePoints", RawPayload = "AUS-2,5" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 4, SectionType = "RacePoints", RawPayload = "DNF,5" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 6, SectionType = "RacePoints", RawPayload = "CHN-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 7, SectionType = "RacePoints", RawPayload = "DNF,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 8, SectionType = "TotalsMeta", RawPayload = "Result,30" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        var result = await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(5, result.LegacyPickScoreCount);

        var legacyScores = await dbContext.MigrationImportLegacyPickScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync();

        Assert.Equal("albert_park", legacyScores[0].RaceCode);
        Assert.Equal("albert_park", legacyScores[1].RaceCode);
        Assert.Equal("albert_park", legacyScores[2].RaceCode);
        Assert.Equal("shanghai", legacyScores[3].RaceCode);
        Assert.Equal("shanghai", legacyScores[4].RaceCode);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenSameSourceRaceCodeRepeatsAcrossBlocks_UsesRowRangeCircuitMapping()
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

        dbContext.MigrationImportRaceRoundMappings.AddRange(
            new MigrationImportRaceRoundMappingEntity
            {
                ImportRunId = runId,
                RaceSequence = 1,
                SourceRowNumber = 2,
                SourceRaceCode = "AUS",
                MappedCircuitId = "albert_park"
            },
            new MigrationImportRaceRoundMappingEntity
            {
                ImportRunId = runId,
                RaceSequence = 2,
                SourceRowNumber = 6,
                SourceRaceCode = "AUS",
                MappedCircuitId = "red_bull_ring"
            });

        dbContext.MigrationImportRawRows.AddRange(
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePoints", RawPayload = "AUS-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePoints", RawPayload = "AUS-2,5" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 4, SectionType = "RacePoints", RawPayload = "DNF,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 6, SectionType = "RacePoints", RawPayload = "AUS-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 7, SectionType = "RacePoints", RawPayload = "DNF,5" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 8, SectionType = "TotalsMeta", RawPayload = "Result,30" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        var legacyScores = await dbContext.MigrationImportLegacyPickScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync();

        Assert.Equal("albert_park", legacyScores[0].RaceCode);
        Assert.Equal("albert_park", legacyScores[1].RaceCode);
        Assert.Equal("albert_park", legacyScores[2].RaceCode);
        Assert.Equal("red_bull_ring", legacyScores[3].RaceCode);
        Assert.Equal("red_bull_ring", legacyScores[4].RaceCode);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenPhilContractDryRunHasSecondAusBlock_MapsToAustriaBySequenceFallback()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = $"/tmp/{MigrationPhil2025CsvContractPolicy.SourceFileName}",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportRawRows.AddRange(
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePoints", RawPayload = "AUS-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePoints", RawPayload = "CHN-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 4, SectionType = "RacePoints", RawPayload = "JPN-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 5, SectionType = "RacePoints", RawPayload = "BAH-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 6, SectionType = "RacePoints", RawPayload = "SAR-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 7, SectionType = "RacePoints", RawPayload = "MIA-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 8, SectionType = "RacePoints", RawPayload = "IMO-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 9, SectionType = "RacePoints", RawPayload = "MON-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 10, SectionType = "RacePoints", RawPayload = "BAR-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 11, SectionType = "RacePoints", RawPayload = "CAN-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 12, SectionType = "RacePoints", RawPayload = "AUS-1,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 13, SectionType = "RacePoints", RawPayload = "AUS-2,5" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 14, SectionType = "TotalsMeta", RawPayload = "Result,115" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        var result = await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(12, result.LegacyPickScoreCount);

        var firstAus = await dbContext.MigrationImportLegacyPickScores
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.PickType == "1" && x.Subject == "Philip");
        Assert.Equal("albert_park", firstAus.RaceCode);

        var secondAusWinner = await dbContext.MigrationImportLegacyPickScores
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 12 && x.PickType == "1" && x.Subject == "Philip");
        Assert.Equal("red_bull_ring", secondAusWinner.RaceCode);

        var secondAusSecondPlace = await dbContext.MigrationImportLegacyPickScores
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 13 && x.PickType == "2" && x.Subject == "Philip");
        Assert.Equal("red_bull_ring", secondAusSecondPlace.RaceCode);
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
