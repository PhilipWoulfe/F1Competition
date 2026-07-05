using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationRaceSelectionParserTests
{
    [Fact]
    public async Task ParseAndPersistAsync_WhenRaceRowsExist_ExtractsParticipantPicksAndActualOutcome()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "AUS-1,VER,NOR,PIA,LEC" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePick", RawPayload = "DNF,NONE,NOT,,SAI DOO" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parsedCount = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(8, parsedCount);

        var selections = await dbContext.MigrationImportRaceSelections
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(8, selections.Count);

        var ausWinner = selections.Single(x => x.RowNumber == 2 && x.Subject == "Philip" && !x.IsActualOutcome);
        Assert.Equal("AUS", ausWinner.RaceCode);
        Assert.Equal("1", ausWinner.PickType);
        Assert.Equal("VER", ausWinner.NormalizedValue);

        var dnfPhilip = selections.Single(x => x.RowNumber == 3 && x.Subject == "Philip" && !x.IsActualOutcome);
        Assert.Null(dnfPhilip.NormalizedValue);

        var dnfAndy = selections.Single(x => x.RowNumber == 3 && x.Subject == "Andy" && !x.IsActualOutcome);
        Assert.Null(dnfAndy.NormalizedValue);

        var dnfActual = selections.Single(x => x.RowNumber == 3 && x.Subject == "ACTUAL" && x.IsActualOutcome);
        Assert.Equal("SAI DOO", dnfActual.NormalizedValue);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenExplicitLRowExists_ParsesActualOutcomeFromLabeledRow()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "AUS-1,VER,NOR,PIA,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePick", RawPayload = "L-AUS-2,,, ,NOR" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        var lRowActual = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 3 && x.Subject == "ACTUAL");

        Assert.Equal("AUS", lRowActual.RaceCode);
        Assert.Equal("2", lRowActual.PickType);
        Assert.Equal("NOR", lRowActual.NormalizedValue);
        Assert.True(lRowActual.IsActualOutcome);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenNoHeaderParticipants_ReturnsZero()
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

        dbContext.MigrationImportRawRows.Add(new MigrationImportRawRowEntity
        {
            ImportRunId = runId,
            RowNumber = 1,
            SectionType = "RacePick",
            RawPayload = "AUS-1,VER,NOR"
        });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parsedCount = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(0, parsedCount);
        Assert.Empty(await dbContext.MigrationImportRaceSelections.ToListAsync());
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m3-parser-{Guid.NewGuid():N}")
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