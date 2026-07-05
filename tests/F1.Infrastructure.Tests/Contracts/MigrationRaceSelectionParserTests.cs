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
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(8, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

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
        Assert.Empty(await dbContext.MigrationImportUnresolvedTokens
            .Where(x => x.ImportRunId == runId)
            .ToListAsync());
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
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

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
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(0, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);
        Assert.Empty(await dbContext.MigrationImportRaceSelections.ToListAsync());
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenAliasTokensProvided_NormalizesCaseAndWhitespaceVariants()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "AUS-1, max , hulk ,   Bear   Man  ,not" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(4, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var selections = await dbContext.MigrationImportRaceSelections
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal("HUL", selections.Single(x => x.Subject == "Andy").NormalizedValue);
        Assert.Equal("BEA", selections.Single(x => x.Subject == "BINGPT").NormalizedValue);
        Assert.Equal("VER", selections.Single(x => x.Subject == "Philip").NormalizedValue);
        Assert.Null(selections.Single(x => x.Subject == "ACTUAL").NormalizedValue);
        Assert.Empty(await dbContext.MigrationImportUnresolvedTokens.ToListAsync());
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenUnknownTokenProvided_PersistsUnresolvedTokenWithoutAutoNormalization()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,Andy,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "AUS-1,verstappen,VER,MAXX" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(3, parseResult.SelectionCount);
        Assert.Equal(2, parseResult.UnresolvedTokenCount);

        var philipSelection = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "Philip");
        Assert.Equal("verstappen", philipSelection.NormalizedValue);

        var unresolved = await dbContext.MigrationImportUnresolvedTokens
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(2, unresolved.Count);
        Assert.Equal("ACTUAL", unresolved[0].Subject);
        Assert.Equal("MAXX", unresolved[0].RawToken);
        Assert.Equal("Philip", unresolved[1].Subject);
        Assert.Equal("verstappen", unresolved[1].RawToken);
        Assert.All(unresolved, token =>
        {
            Assert.Equal("AUS", token.RaceCode);
            Assert.Equal("1", token.PickType);
            Assert.Equal(2, token.RowNumber);
        });
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenDnfContainsMultiTokenActual_OnlyPersistsUnknownTokens()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Kevin,Veronica,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "AUS-1,VER,NOR,PIA" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePick", RawPayload = "DNF,BORT,NOT,SAI DOO BOR LAW ALO HAD" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(6, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var dnfActual = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 3 && x.Subject == "ACTUAL" && x.PickType == "DNF");
        Assert.Equal("SAI DOO BOR LAW ALO HAD", dnfActual.NormalizedValue);

        var unresolved = await dbContext.MigrationImportUnresolvedTokens
            .Where(x => x.ImportRunId == runId)
            .ToListAsync();
        Assert.Empty(unresolved);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenLeecAliasProvided_NormalizesToLec()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,New Sexy Ayrton,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "BRA-3,LEEC,LEC" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(2, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var participantPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "New Sexy Ayrton");

        Assert.Equal("LEC", participantPick.NormalizedValue);
        Assert.Empty(await dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId).ToListAsync());
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