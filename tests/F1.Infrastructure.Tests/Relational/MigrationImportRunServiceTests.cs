using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace F1.Infrastructure.Tests.Relational;

[Collection(PostgresContractCollection.Name)]
public sealed class MigrationImportRunServiceTests
{
    private readonly PostgresTestContainerFixture _fixture;

    public MigrationImportRunServiceTests(PostgresTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartRunAsync_WhenFileUnchanged_ProducesStableChecksumAcrossRuns()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync("AUS-1,NOR\nAUS-2,PIA\n");

        try
        {
            var service = new MigrationImportRunService(new TestDbContextFactory(_fixture.ConnectionString));

            var firstRun = await service.StartRunAsync(sourceFilePath, isDryRun: true, CancellationToken.None);
            var secondRun = await service.StartRunAsync(sourceFilePath, isDryRun: true, CancellationToken.None);

            Assert.Equal(firstRun.SourceFileChecksum, secondRun.SourceFileChecksum);

            await using var verificationContext = CreateContext();
            var runs = await verificationContext.MigrationImportRuns.AsNoTracking().ToListAsync();
            Assert.Equal(2, runs.Count);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunLifecycle_WhenCompletedOrFailed_PersistsExpectedStatusAndMetadata()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync("AUS-1,NOR\n");

        try
        {
            var service = new MigrationImportRunService(new TestDbContextFactory(_fixture.ConnectionString));

            var completedRun = await service.StartRunAsync(sourceFilePath, isDryRun: true, CancellationToken.None);
            await service.CompleteRunAsync(completedRun.RunId, rawRowCount: 15, CancellationToken.None);

            var failedRun = await service.StartRunAsync(sourceFilePath, isDryRun: false, CancellationToken.None);
            await service.FailRunAsync(failedRun.RunId, "simulated failure", CancellationToken.None);

            await using var verificationContext = CreateContext();
            var runs = await verificationContext.MigrationImportRuns.AsNoTracking().OrderBy(x => x.StartedAtUtc).ToListAsync();

            Assert.Equal("Completed", runs[0].Status);
            Assert.Equal(15, runs[0].RawRowCount);
            Assert.NotNull(runs[0].FinishedAtUtc);

            Assert.Equal("Failed", runs[1].Status);
            Assert.Equal("simulated failure", runs[1].ErrorMessage);
            Assert.NotNull(runs[1].FinishedAtUtc);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenDryRunEnabled_StagesRowsWithoutCreatingDomainEntities()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync("Question,Philip\nAUS-1,NOR\nBAH-HUMBUG,NONE\n");

        try
        {
            var dbFactory = new TestDbContextFactory(_fixture.ConnectionString);
            var runService = new MigrationImportRunService(dbFactory);
            var jolpicaClient = new TrackingJolpicaClient();

            var orchestrator = new MigrationImportOrchestrator(
                NullLogger<MigrationImportOrchestrator>.Instance,
                runService,
                new MigrationImportRowClassifier(),
                new MigrationRaceSelectionParser(dbFactory),
                new MigrationRaceRoundMapper(
                    dbFactory,
                    jolpicaClient,
                    Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
                    Options.Create(new MigrationImportOptions { Season = 2025 })),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true
                }));

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            Assert.Empty(await verificationContext.Competitions.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Drivers.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Races.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Selections.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.MigrationImportJolpicaRaceSnapshots.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.MigrationImportRaceRoundMappings.AsNoTracking().ToListAsync());
            Assert.Equal(0, jolpicaClient.GetRacesCallCount);

            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.True(run.IsDryRun);
            Assert.Equal("Completed", run.Status);
            Assert.Equal(3, run.RawRowCount);

            var stagedRows = await verificationContext.MigrationImportRawRows.AsNoTracking().OrderBy(x => x.RowNumber).ToListAsync();
            Assert.Equal(3, stagedRows.Count);
            Assert.Equal("Header", stagedRows[0].SectionType);
            Assert.Equal("RacePick", stagedRows[1].SectionType);
            Assert.Equal("RacePick", stagedRows[2].SectionType);
            Assert.Equal("Mapped BAH-HUMBUG label to DNF pick type.", stagedRows[2].ClassificationReason);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenUnresolvedTokensReachThreshold_FailsRun()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync("Question,Philip\nAUS-1,MAXX\n");

        try
        {
            var dbFactory = new TestDbContextFactory(_fixture.ConnectionString);
            var runService = new MigrationImportRunService(dbFactory);

            var orchestrator = new MigrationImportOrchestrator(
                NullLogger<MigrationImportOrchestrator>.Instance,
                runService,
                new MigrationImportRowClassifier(),
                new MigrationRaceSelectionParser(dbFactory),
                new MigrationRaceRoundMapper(
                    dbFactory,
                    new TrackingJolpicaClient(),
                    Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
                    Options.Create(new MigrationImportOptions { Season = 2025 })),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    UnresolvedTokenFailThreshold = 1
                }));

            await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunOnceAsync(CancellationToken.None));

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Failed", run.Status);
            Assert.NotNull(run.ErrorMessage);

            var unresolved = await verificationContext.MigrationImportUnresolvedTokens
                .AsNoTracking()
                .Where(x => x.ImportRunId == run.Id)
                .ToListAsync();
            Assert.Single(unresolved);
            Assert.Equal("MAXX", unresolved[0].RawToken);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenUnresolvedTokensBelowThreshold_CompletesWithWarnings()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync("Question,Philip\nAUS-1,MAXX,VER\n");

        try
        {
            var dbFactory = new TestDbContextFactory(_fixture.ConnectionString);
            var runService = new MigrationImportRunService(dbFactory);

            var orchestrator = new MigrationImportOrchestrator(
                NullLogger<MigrationImportOrchestrator>.Instance,
                runService,
                new MigrationImportRowClassifier(),
                new MigrationRaceSelectionParser(dbFactory),
                new MigrationRaceRoundMapper(
                    dbFactory,
                    new TrackingJolpicaClient(),
                    Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
                    Options.Create(new MigrationImportOptions { Season = 2025 })),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    UnresolvedTokenFailThreshold = 2
                }));

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Completed", run.Status);

            var unresolved = await verificationContext.MigrationImportUnresolvedTokens
                .AsNoTracking()
                .Where(x => x.ImportRunId == run.Id)
                .ToListAsync();
            Assert.Single(unresolved);
            Assert.Equal("MAXX", unresolved[0].RawToken);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    private static async Task<string> CreateTempCsvAsync(string content)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"f1-migration-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(tempPath, content);
        return tempPath;
    }

    private F1DbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<F1DbContext>()
            .UseNpgsql(_fixture.ConnectionString)
            .Options;

        return new F1DbContext(options);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<F1DbContext>
    {
        private readonly DbContextOptions<F1DbContext> _options;

        public TestDbContextFactory(string connectionString)
        {
            _options = new DbContextOptionsBuilder<F1DbContext>()
                .UseNpgsql(connectionString)
                .Options;
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

    private sealed class TrackingJolpicaClient : IJolpicaClient
    {
        public int GetRacesCallCount { get; private set; }

        public Task<IReadOnlyList<JolpicaDriverDto>> GetDriversAsync(int season, int retryCount, int retryDelayMs, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<JolpicaDriverDto>>([]);
        }

        public Task<IReadOnlyList<JolpicaRaceDto>> GetRacesAsync(int season, int retryCount, int retryDelayMs, CancellationToken cancellationToken)
        {
            GetRacesCallCount++;

            IReadOnlyList<JolpicaRaceDto> races =
            [
                new() { Season = "2025", Round = "1", RaceName = "Australian Grand Prix", Date = "2025-03-16", Time = "05:00:00Z" },
                new() { Season = "2025", Round = "2", RaceName = "Chinese Grand Prix", Date = "2025-03-23", Time = "07:00:00Z" }
            ];
            return Task.FromResult(races);
        }
    }
}