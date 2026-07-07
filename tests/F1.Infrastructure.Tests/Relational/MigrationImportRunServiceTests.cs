using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
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
            Assert.Equal("NotDetected", runs[0].PreseasonParseStatus);
            Assert.Equal("NotDetected", runs[0].PreseasonScoringStatus);
            Assert.Equal(0, runs[0].PreseasonWarningCount);
            Assert.Equal(0, runs[0].PreseasonErrorCount);
            Assert.True(runs[0].PreseasonIsolationGuardPassed);

            Assert.Equal("Failed", runs[1].Status);
            Assert.Equal("simulated failure", runs[1].ErrorMessage);
            Assert.NotNull(runs[1].FinishedAtUtc);
            Assert.Equal("NotDetected", runs[1].PreseasonParseStatus);
            Assert.Equal("NotDetected", runs[1].PreseasonScoringStatus);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenWriteModeEnabled_DoesNotPersistCanonicalRaceDomainEntitiesYet()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync(
            "Question,Philip,,\n" +
            "AUS-1,VER,VER\n" +
            "DNF,NONE,\n" +
            "AUS-1,10,10\n" +
            "DNF,5,5\n" +
            "Result,15\n");

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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = false,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Completed", run.Status);

            Assert.NotEmpty(await verificationContext.MigrationImportRaceSelections.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.MigrationImportCalculatedScores.AsNoTracking().ToListAsync());

            // Story 1 characterization: write mode currently does not materialize race-domain canonical tables.
            Assert.Empty(await verificationContext.Drivers.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Races.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Selections.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.SelectionPositions.AsNoTracking().ToListAsync());
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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            Assert.Empty(await verificationContext.Competitions.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Drivers.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Races.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Selections.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.MigrationImportJolpicaRaceSnapshots.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.MigrationImportRaceRoundMappings.AsNoTracking().ToListAsync());
            Assert.True(jolpicaClient.GetRacesCallCount > 0);

            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.True(run.IsDryRun);
            Assert.Equal("Completed", run.Status);
            Assert.Equal(3, run.RawRowCount);
            Assert.Equal("NotDetected", run.PreseasonParseStatus);
            Assert.Equal("NotDetected", run.PreseasonScoringStatus);
            Assert.Equal(0, run.PreseasonWarningCount);
            Assert.Equal(0, run.PreseasonErrorCount);
            Assert.True(run.PreseasonIsolationGuardPassed);
            Assert.NotNull(run.ParitySnapshotChecksum);
            Assert.Equal("NotCompared", run.ParityStatus);

            var stagedRows = await verificationContext.MigrationImportRawRows.AsNoTracking().OrderBy(x => x.RowNumber).ToListAsync();
            Assert.Equal(3, stagedRows.Count);
            Assert.Equal("Header", stagedRows[0].SectionType);
            Assert.Equal("RacePick", stagedRows[1].SectionType);
            Assert.Equal("RacePick", stagedRows[2].SectionType);
            Assert.Equal("Mapped special label to DNF pick type.", stagedRows[2].ClassificationReason);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenDryAndWriteUseSameSource_ParityChecksumsMatch()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync(
            "Question,Philip,,\n" +
            "AUS-1,VER,VER\n" +
            "DNF,NONE,\n" +
            "AUS-1,10,10\n" +
            "DNF,5,5\n" +
            "Result,15\n");

        try
        {
            var dbFactory = new TestDbContextFactory(_fixture.ConnectionString);
            var runService = new MigrationImportRunService(dbFactory);

            var dryRunOrchestrator = new MigrationImportOrchestrator(
                NullLogger<MigrationImportOrchestrator>.Instance,
                runService,
                new MigrationImportRowClassifier(),
                new MigrationRaceSelectionParser(dbFactory),
                new MigrationRaceRoundMapper(
                    dbFactory,
                    new TrackingJolpicaClient(),
                    Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
                    Options.Create(new MigrationImportOptions { Season = 2025 })),
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            var writeRunOrchestrator = new MigrationImportOrchestrator(
                NullLogger<MigrationImportOrchestrator>.Instance,
                runService,
                new MigrationImportRowClassifier(),
                new MigrationRaceSelectionParser(dbFactory),
                new MigrationRaceRoundMapper(
                    dbFactory,
                    new TrackingJolpicaClient(),
                    Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
                    Options.Create(new MigrationImportOptions { Season = 2025 })),
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = false,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await dryRunOrchestrator.RunOnceAsync(CancellationToken.None);
            await writeRunOrchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var runs = await verificationContext.MigrationImportRuns
                .AsNoTracking()
                .OrderBy(x => x.StartedAtUtc)
                .ToListAsync();

            Assert.Equal(2, runs.Count);
            Assert.Equal("Completed", runs[0].Status);
            Assert.Equal("Completed", runs[1].Status);
            Assert.NotNull(runs[0].ParitySnapshotChecksum);
            Assert.NotNull(runs[1].ParitySnapshotChecksum);
            Assert.Equal(runs[0].ParitySnapshotChecksum, runs[1].ParitySnapshotChecksum);
            Assert.Equal("NotCompared", runs[0].ParityStatus);
            Assert.Equal("Matched", runs[1].ParityStatus);
            Assert.Equal(runs[0].Id, runs[1].ParityComparedRunId);
            Assert.Equal(runs[0].ParitySnapshotChecksum, runs[1].ParityComparedChecksum);
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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    UnresolvedTokenFailThreshold = 1
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunOnceAsync(CancellationToken.None));

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Failed", run.Status);
            Assert.NotNull(run.ErrorMessage);
            Assert.Equal("Failed", run.PreseasonParseStatus);
            Assert.Equal("Failed", run.PreseasonScoringStatus);
            Assert.Equal(1, run.PreseasonErrorCount);
            Assert.False(run.PreseasonIsolationGuardPassed);

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
    public async Task RunOnceAsync_WhenPhilPolicyPresent_ScoresPreseasonWithoutPolicyMissingWarnings()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync(
            string.Join(Environment.NewLine,
            [
                "Question,Philip,New Sexy Ayrton,Andy,Claire,Dave,Kevin,Pious ,Shane,Veronica,BINGPT,,",
                "At least one driver will win 4 consecutive races?,Y,N,Y,Y,N,Y,Y,Y,N,Y,N,20"
            ]),
            MigrationPhil2025CsvContractPolicy.SourceFileName);

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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Completed", run.Status);
            Assert.Equal("Completed", run.PreseasonParseStatus);
            Assert.Equal("Completed", run.PreseasonScoringStatus);
            Assert.Equal(0, run.PreseasonWarningCount);

            var daveScore = await verificationContext.MigrationImportPreseasonCalculatedScores
                .AsNoTracking()
                .SingleAsync(x => x.ImportRunId == run.Id && x.QuestionKey == "PRE-002" && x.Subject == "Dave");
            Assert.Equal(20, daveScore.Points);
            Assert.Equal("PRESEASON_EXACT", daveScore.ReasonCode);
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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    UnresolvedTokenFailThreshold = 2
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Completed", run.Status);
            Assert.Equal(1, run.UnresolvedTokenCount);

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
    public async Task RunOnceAsync_WhenWriteModeEnabled_RewritesSelectionRaceCodesToMappedCircuitIds()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync(
            "Question,Philip,,\n" +
            "AUS-1,VER,VER\n" +
            "DNF,NONE,\n" +
            "CHN-1,NOR,NOR\n" +
            "DNF,NONE,\n" +
            "AUS-1,10,10\n" +
            "DNF,5,5\n" +
            "CHN-1,10,10\n" +
            "DNF,0,0\n" +
            "Result,25\n");

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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = false,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Completed", run.Status);
            Assert.Equal("NotDetected", run.PreseasonParseStatus);
            Assert.Equal("NotDetected", run.PreseasonScoringStatus);

            var participantSelections = await verificationContext.MigrationImportRaceSelections
                .Where(x => x.ImportRunId == run.Id && x.Subject == "Philip")
                .OrderBy(x => x.RowNumber)
                .ToListAsync();

            Assert.Equal("albert_park", participantSelections[0].RaceCode);
            Assert.Equal("albert_park", participantSelections[1].RaceCode);
            Assert.Equal("shanghai", participantSelections[2].RaceCode);
            Assert.Equal("shanghai", participantSelections[3].RaceCode);

            var legacyScores = await verificationContext.MigrationImportLegacyPickScores
                .Where(x => x.ImportRunId == run.Id)
                .OrderBy(x => x.RowNumber)
                .ToListAsync();

            Assert.Contains(legacyScores, x => x.RaceCode == "albert_park");
            Assert.Contains(legacyScores, x => x.RaceCode == "shanghai");
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenWriteModeEnabled_PersistsCanonicalRaceDomainEntities()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync(
            "Question,Philip,,\n" +
            "AUS-1,VER,VER\n" +
            "DNF,NONE,\n" +
            "AUS-1,10,10\n" +
            "DNF,5,5\n" +
            "Result,15\n");

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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = false,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            Assert.NotEmpty(await verificationContext.Drivers.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.Races.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.Selections.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.SelectionPositions.AsNoTracking().ToListAsync());
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenCanonicalWriteFailsMidTransaction_RollsBackCanonicalEntities()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var sourceFilePath = await CreateTempCsvAsync(
            "Question,Philip,,\n" +
            "AUS-1,VER,VER\n" +
            "DNF,NONE,\n" +
            "AUS-1,10,10\n" +
            "DNF,5,5\n" +
            "Result,15\n");

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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = false,
                    Season = 2025,
                    CanonicalWriteFailureInjectionStage = "after_drivers"
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunOnceAsync(CancellationToken.None));

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Failed", run.Status);

            Assert.Empty(await verificationContext.Drivers.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Races.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Selections.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.SelectionPositions.AsNoTracking().ToListAsync());
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenPhilCsvContainsPreseasonTwentyPointRows_DoesNotImportThemAsRacePoints()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var rows = new List<string> { "Question,Philip,," };

        // Rows 2-21 preseason questions.
        for (var row = 2; row <= 21; row++)
        {
            rows.Add($"Pre-Q-{row},Y,,");
        }

        // Rows 22-41 preseason tallies, intentionally race-like label with 20 points.
        rows.Add("AUS-1,20,,");
        for (var row = 23; row <= 41; row++)
        {
            rows.Add($"Pre-P-{row},0,,");
        }

        // Row 42 spacer.
        rows.Add(",,,");

        // Row 43 race selections begin.
        rows.Add("AUS-1,VER,NOR");

        // Rows 44-138 filler race picks to preserve Phil contract row windows.
        for (var row = 44; row <= 138; row++)
        {
            rows.Add($"R{row}-1,VER,NOR");
        }

        // Row 139 spacer.
        rows.Add(",,,");

        // Row 140 race points (in scope).
        rows.Add("AUS-1,10,,");

        // Rows 141-235 filler race points rows to preserve Phil contract row windows.
        for (var row = 141; row <= 235; row++)
        {
            rows.Add($"R{row}-1,0,,");
        }

        // Row 236 totals.
        rows.Add("Result,10,,");

        var sourceFilePath = await CreateTempCsvAsync(string.Join(Environment.NewLine, rows), MigrationPhil2025CsvContractPolicy.SourceFileName);

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
                new MigrationScoreRecalculator(dbFactory),
                new MigrationLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Completed", run.Status);
            Assert.Equal("CompletedWithWarnings", run.PreseasonParseStatus);
            Assert.Equal("CompletedWithWarnings", run.PreseasonScoringStatus);
            Assert.True(run.PreseasonAnswerCount > 0);
            Assert.True(run.PreseasonScoredQuestionCount > 0);
            Assert.True(run.PreseasonQuestionDiffCount > 0);
            Assert.True(run.PreseasonIsolationGuardPassed);

            var preseasonStaged = await verificationContext.MigrationImportRawRows
                .AsNoTracking()
                .SingleAsync(x => x.ImportRunId == run.Id && x.RowNumber == 22);
            Assert.Equal(MigrationImportSectionTypes.SeasonQuestionPoints, preseasonStaged.SectionType);

            var legacyScores = await verificationContext.MigrationImportLegacyPickScores
                .AsNoTracking()
                .Where(x => x.ImportRunId == run.Id)
                .ToListAsync();

            Assert.Single(legacyScores);
            Assert.Equal(140, legacyScores[0].RowNumber);
            Assert.Equal(10, legacyScores[0].LegacyPoints);
            Assert.DoesNotContain(legacyScores, x => x.LegacyPoints == 20);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenLegacyImporterWritesPreseasonRowAsRacePoints_FailsIsolationGuard()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var rows = new List<string> { "Question,Philip,," };
        for (var row = 2; row <= 21; row++)
        {
            rows.Add($"Pre-Q-{row},Y,,");
        }
        rows.Add("AUS-1,20,,");

        var sourceFilePath = await CreateTempCsvAsync(string.Join(Environment.NewLine, rows), MigrationPhil2025CsvContractPolicy.SourceFileName);

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
                new MigrationScoreRecalculator(dbFactory),
                new ContaminatingLegacyScoreImporter(dbFactory),
                new MigrationReconciliationService(dbFactory),
                dbFactory,
                Options.Create(new DataSyncOptions { AutoMigrate = false }),
                Options.Create(new MigrationImportOptions
                {
                    Enabled = true,
                    SourceFilePath = sourceFilePath,
                    DryRun = true,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunOnceAsync(CancellationToken.None));
            Assert.Contains("Preseason isolation guard failed", exception.Message);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Failed", run.Status);
            Assert.Equal("Failed", run.PreseasonParseStatus);
            Assert.Equal("Failed", run.PreseasonScoringStatus);
            Assert.Equal(1, run.PreseasonErrorCount);
            Assert.False(run.PreseasonIsolationGuardPassed);
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

    private static async Task<string> CreateTempCsvAsync(string content, string fileName)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{fileName}");
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
                new() { Season = "2025", Round = "1", RaceName = "Australian Grand Prix", Date = "2025-03-16", Time = "05:00:00Z", Circuit = new JolpicaCircuitDto { CircuitId = "albert_park", CircuitName = "Albert Park Grand Prix Circuit" } },
                new() { Season = "2025", Round = "2", RaceName = "Chinese Grand Prix", Date = "2025-03-23", Time = "07:00:00Z", Circuit = new JolpicaCircuitDto { CircuitId = "shanghai", CircuitName = "Shanghai International Circuit" } }
            ];
            return Task.FromResult(races);
        }
    }

    private sealed class ContaminatingLegacyScoreImporter : IMigrationLegacyScoreImporter
    {
        private readonly IDbContextFactory<F1DbContext> _dbContextFactory;

        public ContaminatingLegacyScoreImporter(IDbContextFactory<F1DbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<MigrationLegacyScoreImportResult> ImportAndPersistAsync(Guid runId, CancellationToken cancellationToken)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            dbContext.MigrationImportLegacyPickScores.RemoveRange(
                dbContext.MigrationImportLegacyPickScores.Where(x => x.ImportRunId == runId));

            dbContext.MigrationImportLegacyPickScores.Add(new MigrationImportLegacyPickScoreEntity
            {
                ImportRunId = runId,
                RowNumber = 22,
                RaceCode = "AUS",
                PickType = "1",
                Subject = "Philip",
                RawLegacyPoints = "20",
                LegacyPoints = 20
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            return new MigrationLegacyScoreImportResult(
                LegacyPickScoreCount: 1,
                ImportedTotalCount: 0,
                CalculatedTotalCount: 0);
        }
    }
}