using F1.Api.Configuration;
using F1.Api.Services;
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
    public async Task RunOnceAsync_WhenWriteModeEnabled_PersistsCanonicalEntitiesAndImportArtifacts()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

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

            Assert.NotEmpty(await verificationContext.Drivers.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.Races.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.Selections.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.SelectionPositions.AsNoTracking().ToListAsync());
            Assert.NotEmpty(await verificationContext.RacePickScores.AsNoTracking().ToListAsync());
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenDaveWriteModeRunsOnNonEmptyMultiCompetitionDb_WritesOnlyToDaveCompetitionScope()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var philCompetition = new F1.Core.Models.Competition
        {
            Name = "Philip 2025",
            Year = 2025,
            Description = "Phil scope"
        };

        var daveCompetition = new F1.Core.Models.Competition
        {
            Name = "Dave 2025",
            Year = 2025,
            Description = "Dave scope"
        };

        setupContext.Competitions.AddRange(philCompetition, daveCompetition);
        await setupContext.SaveChangesAsync();

        var philRaceId = "philip-2025-1-australian-grand-prix";
        var daveRaceId = "dave-2025-1-australian-grand-prix";

        setupContext.Races.AddRange(
            new F1.Core.Models.Race
            {
                Id = philRaceId,
                CompetitionId = philCompetition.Id,
                Season = 2025,
                Round = 1,
                RaceName = "Australian Grand Prix",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            },
            new F1.Core.Models.Race
            {
                Id = daveRaceId,
                CompetitionId = daveCompetition.Id,
                Season = 2025,
                Round = 1,
                RaceName = "Australian Grand Prix",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            });

        setupContext.Drivers.Add(new F1.Core.Models.Driver
        {
            DriverId = "OLD",
            FullName = "Legacy Driver",
            Code = "OLD"
        });

        var philSelectionId = Guid.NewGuid();
        setupContext.Selections.Add(new F1.Core.Models.Selection
        {
            Id = philSelectionId,
            UserId = "Alice",
            RaceId = philRaceId,
            BetType = F1.Core.Models.BetType.Regular,
            SubmittedAtUtc = DateTime.UtcNow
        });
        setupContext.SelectionPositions.Add(new SelectionPositionEntity
        {
            SelectionId = philSelectionId,
            Position = 1,
            DriverId = "OLD"
        });

        setupContext.QuestionTemplates.AddRange(
            new QuestionTemplateEntity
            {
                CompetitionId = philCompetition.Id,
                Season = 2025,
                QuestionId = "H2H-R01",
                Category = F1.Core.Models.QuestionCategory.H2H,
                Prompt = "R01 H2H",
                OptionsJson = "{\"pointsForCorrectPick\":5}",
                Status = F1.Core.Models.QuestionTemplateStatus.Published,
                SortOrder = 100,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new QuestionTemplateEntity
            {
                CompetitionId = daveCompetition.Id,
                Season = 2025,
                QuestionId = "H2H-R01",
                Category = F1.Core.Models.QuestionCategory.H2H,
                Prompt = "R01 H2H",
                OptionsJson = "{\"pointsForCorrectPick\":5}",
                Status = F1.Core.Models.QuestionTemplateStatus.Published,
                SortOrder = 100,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        await setupContext.SaveChangesAsync();

        var philTemplateId = await setupContext.QuestionTemplates
            .Where(x => x.CompetitionId == philCompetition.Id && x.QuestionId == "H2H-R01")
            .Select(x => x.Id)
            .SingleAsync();

        setupContext.QuestionAnswers.Add(new QuestionAnswerEntity
        {
            QuestionTemplateId = philTemplateId,
            ParticipantId = "Alice",
            ImportedAnswer = "norris",
            OverrideAnswer = null,
            RecordedAtUtc = DateTime.UtcNow
        });
        setupContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = philTemplateId,
            ImportedAnswer = "max_verstappen",
            OverrideAnswer = null,
            RecordedAtUtc = DateTime.UtcNow
        });
        setupContext.QuestionScores.Add(new QuestionScoreEntity
        {
            QuestionTemplateId = philTemplateId,
            ParticipantId = "Alice",
            ImportedPoints = 7,
            CalculatedPoints = 7,
            DeltaPoints = 0,
            RecordedAtUtc = DateTime.UtcNow
        });
        await setupContext.SaveChangesAsync();

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"f1-dave-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        File.WriteAllText(
            Path.Combine(tempDirectory, Dave2025SourcePackageContract.RacesFile),
            string.Join(
                Environment.NewLine,
                [
                    "Name,Race1-PQ,Race1-1,Race1-2,Race1-3,Race1-DNF,Race1-H2H",
                    "_Result,,NOR,VER,PIA,None,VER",
                    "Alice,Yes,NOR,PIA,VER,None,NOR"
                ]));
        File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusFile), "Question,Alice");
        File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusAnswersFile), "Question,Answer");
        File.WriteAllText(
            Path.Combine(tempDirectory, Dave2025SourcePackageContract.LeaderboardFile),
            string.Join(Environment.NewLine, ["Name,Race Points,Bonus Points,Total", "Alice,25,0,25"]));

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
                    SourceFilePath = tempDirectory,
                    DryRun = false,
                    Season = 2025
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().SingleAsync();
            Assert.Equal("Completed", run.Status);

            var philSelection = await verificationContext.Selections
                .AsNoTracking()
                .SingleAsync(x => x.Id == philSelectionId);
            Assert.Equal(philRaceId, philSelection.RaceId);

            var daveSelection = await verificationContext.Selections
                .AsNoTracking()
                .SingleAsync(x => x.RaceId == daveRaceId && x.UserId == "Alice");
            Assert.NotEqual(philSelectionId, daveSelection.Id);

            var philPosition = await verificationContext.SelectionPositions
                .AsNoTracking()
                .SingleAsync(x => x.SelectionId == philSelectionId && x.Position == 1);
            Assert.Equal("OLD", philPosition.DriverId);

            var daveTemplateCount = await verificationContext.QuestionTemplates
                .Where(x =>
                    x.CompetitionId == daveCompetition.Id &&
                    x.Season == 2025 &&
                    x.Category == F1.Core.Models.QuestionCategory.H2H &&
                    x.QuestionId.StartsWith("H2H-"))
                .CountAsync();
            Assert.True(daveTemplateCount > 0);

            var philScore = await verificationContext.QuestionScores
                .AsNoTracking()
                .SingleAsync(x => x.QuestionTemplateId == philTemplateId && x.ParticipantId == "Alice");
            Assert.Equal(7, philScore.CalculatedPoints);

            var philScoreCount = await verificationContext.QuestionScores
                .AsNoTracking()
                .CountAsync(x => x.QuestionTemplateId == philTemplateId && x.ParticipantId == "Alice");
            Assert.Equal(1, philScoreCount);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenMigrationStagingRowsAreDeleted_LeaderboardStillReadsCanonicalScores()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

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

            await using (var mutationContext = CreateContext())
            {
                mutationContext.MigrationImportPickDiffs.RemoveRange(mutationContext.MigrationImportPickDiffs);
                mutationContext.MigrationImportParticipantDeltaSummaries.RemoveRange(mutationContext.MigrationImportParticipantDeltaSummaries);
                mutationContext.MigrationImportPreseasonQuestionDiffs.RemoveRange(mutationContext.MigrationImportPreseasonQuestionDiffs);
                mutationContext.MigrationImportPreseasonParticipantDeltaSummaries.RemoveRange(mutationContext.MigrationImportPreseasonParticipantDeltaSummaries);
                mutationContext.MigrationImportCalculatedScores.RemoveRange(mutationContext.MigrationImportCalculatedScores);
                mutationContext.MigrationImportLegacyPickScores.RemoveRange(mutationContext.MigrationImportLegacyPickScores);
                mutationContext.MigrationImportRuns.RemoveRange(mutationContext.MigrationImportRuns);
                await mutationContext.SaveChangesAsync();
            }

            await using var verificationContext = CreateContext();
            var service = new CompetitionLeaderboardService(
                verificationContext,
                Options.Create(new CompetitionLeaderboardOptions
                {
                    Contexts =
                    [
                        new CompetitionLeaderboardContextOption
                        {
                            CompetitionSlug = "migration",
                            Season = 2025,
                            DisplayName = "Migration Import 2025",
                            SourceType = "Canonical",
                            ActiveScoreSource = "ImportedLegacy"
                        }
                    ]
                }));

            var leaderboard = await service.GetLeaderboardAsync("migration", 2025, "active", isAdmin: false, CancellationToken.None);
            var detail = await service.GetParticipantDetailAsync("migration", 2025, "Philip", CancellationToken.None);

            Assert.True(leaderboard.IsDataAvailable);
            Assert.Single(leaderboard.Items);
            Assert.Equal("Philip", leaderboard.Items[0].ParticipantName);
            Assert.Equal(15, leaderboard.Items[0].DisplayPoints);

            Assert.NotEmpty(detail.RacePicks.Items);
            Assert.Equal(15, detail.RacePicks.ImportedTotalPoints);
            Assert.Equal(15, detail.RacePicks.RecalculatedTotalPoints);
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
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

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
    public async Task RunOnceAsync_WhenWriteRunIsRepeatedWithSameChecksum_DoesNotDuplicateCanonicalRows()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

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

            var firstRun = new MigrationImportOrchestrator(
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
                Options.Create(new MigrationImportOptions { Enabled = true, SourceFilePath = sourceFilePath, DryRun = false, Season = 2025 }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            var secondRun = new MigrationImportOrchestrator(
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
                Options.Create(new MigrationImportOptions { Enabled = true, SourceFilePath = sourceFilePath, DryRun = false, Season = 2025 }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await firstRun.RunOnceAsync(CancellationToken.None);

            await using var firstSnapshotContext = CreateContext();
            var firstSelectionCount = await firstSnapshotContext.Selections.CountAsync();
            var firstPositionCount = await firstSnapshotContext.SelectionPositions.CountAsync();

            await secondRun.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var secondSelectionCount = await verificationContext.Selections.CountAsync();
            var secondPositionCount = await verificationContext.SelectionPositions.CountAsync();

            Assert.Equal(firstSelectionCount, secondSelectionCount);
            Assert.Equal(firstPositionCount, secondPositionCount);

            var runs = await verificationContext.MigrationImportRuns.AsNoTracking().OrderBy(x => x.StartedAtUtc).ToListAsync();
            Assert.Equal(2, runs.Count);
            Assert.Equal("FirstWrite", runs[0].IdempotencyOutcome);
            Assert.Equal("Replayed", runs[1].IdempotencyOutcome);
            Assert.Equal(runs[0].IdempotencyScopeKey, runs[1].IdempotencyScopeKey);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenWriteRunChecksumChanges_UsesNewIdempotencyScope()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

        var sourceFilePathA = await CreateTempCsvAsync(
            "Question,Philip,,\n" +
            "AUS-1,VER,VER\n" +
            "DNF,NONE,\n" +
            "AUS-1,10,10\n" +
            "DNF,5,5\n" +
            "Result,15\n");

        var sourceFilePathB = await CreateTempCsvAsync(
            "Question,Philip,,\n" +
            "AUS-1,NOR,VER\n" +
            "DNF,NONE,\n" +
            "AUS-1,0,10\n" +
            "DNF,5,5\n" +
            "Result,5\n");

        try
        {
            var dbFactory = new TestDbContextFactory(_fixture.ConnectionString);
            var runService = new MigrationImportRunService(dbFactory);

            var runA = new MigrationImportOrchestrator(
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
                Options.Create(new MigrationImportOptions { Enabled = true, SourceFilePath = sourceFilePathA, DryRun = false, Season = 2025 }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            var runB = new MigrationImportOrchestrator(
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
                Options.Create(new MigrationImportOptions { Enabled = true, SourceFilePath = sourceFilePathB, DryRun = false, Season = 2025 }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await runA.RunOnceAsync(CancellationToken.None);
            await runB.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var runs = await verificationContext.MigrationImportRuns.AsNoTracking().OrderBy(x => x.StartedAtUtc).ToListAsync();
            Assert.Equal(2, runs.Count);
            Assert.NotEqual(runs[0].SourceFileChecksum, runs[1].SourceFileChecksum);
            Assert.NotEqual(runs[0].IdempotencyScopeKey, runs[1].IdempotencyScopeKey);
            Assert.Equal("FirstWrite", runs[0].IdempotencyOutcome);
            Assert.Equal("FirstWrite", runs[1].IdempotencyOutcome);
        }
        finally
        {
            File.Delete(sourceFilePathA);
            File.Delete(sourceFilePathB);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenConflictPolicyFail_RecordsDiagnosticsAndFailsRun()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var competition = new F1.Core.Models.Competition
        {
            Name = "Migration Import 2025",
            Year = 2025,
            Description = "Seeded for conflict policy tests"
        };
        setupContext.Competitions.Add(competition);
        await setupContext.SaveChangesAsync();

        setupContext.Drivers.Add(new F1.Core.Models.Driver { DriverId = "OLD", FullName = "Existing Driver", Code = "OLD" });
        setupContext.Races.Add(new F1.Core.Models.Race
        {
            Id = "migration-2025-albert-park",
            CompetitionId = competition.Id,
            Season = 2025,
            Round = 1,
            RaceName = "albert_park",
            CircuitName = "albert_park",
            StartTimeUtc = DateTime.UtcNow,
            PreQualyDeadlineUtc = DateTime.UtcNow,
            FinalDeadlineUtc = DateTime.UtcNow
        });
        var existingSelectionId = Guid.NewGuid();
        setupContext.Selections.Add(new F1.Core.Models.Selection
        {
            Id = existingSelectionId,
            UserId = "Philip",
            RaceId = "migration-2025-albert-park",
            BetType = F1.Core.Models.BetType.Regular,
            SubmittedAtUtc = DateTime.UtcNow
        });
        setupContext.SelectionPositions.Add(new SelectionPositionEntity
        {
            SelectionId = existingSelectionId,
            Position = 1,
            DriverId = "OLD"
        });
        await setupContext.SaveChangesAsync();

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
                    CanonicalConflictPolicy = "fail"
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunOnceAsync(CancellationToken.None));

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).FirstAsync();
            Assert.Equal("Failed", run.Status);

            var diagnostics = await verificationContext.MigrationImportConflictDiagnostics
                .AsNoTracking()
                .Where(x => x.ImportRunId == run.Id)
                .ToListAsync();
            Assert.NotEmpty(diagnostics);
            Assert.Contains(diagnostics, x => x.EntityType == "Selection" && x.PolicyOutcome == "Failed");
            Assert.All(diagnostics, x => Assert.Contains("competitionId:", x.KeyFields, StringComparison.OrdinalIgnoreCase));
            Assert.All(diagnostics, x => Assert.Contains("competitionId:", x.SourceReference, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task RunOnceAsync_WhenConflictPolicySkip_RecordsDiagnosticsAndSkipsOverwrite()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var competition = new F1.Core.Models.Competition
        {
            Name = "Migration Import 2025",
            Year = 2025,
            Description = "Seeded for conflict policy tests"
        };
        setupContext.Competitions.Add(competition);
        await setupContext.SaveChangesAsync();

        setupContext.Drivers.Add(new F1.Core.Models.Driver { DriverId = "OLD", FullName = "Existing Driver", Code = "OLD" });
        setupContext.Races.Add(new F1.Core.Models.Race
        {
            Id = "migration-2025-albert-park",
            CompetitionId = competition.Id,
            Season = 2025,
            Round = 1,
            RaceName = "albert_park",
            CircuitName = "albert_park",
            StartTimeUtc = DateTime.UtcNow,
            PreQualyDeadlineUtc = DateTime.UtcNow,
            FinalDeadlineUtc = DateTime.UtcNow
        });
        var existingSelectionId = Guid.NewGuid();
        setupContext.Selections.Add(new F1.Core.Models.Selection
        {
            Id = existingSelectionId,
            UserId = "Philip",
            RaceId = "migration-2025-albert-park",
            BetType = F1.Core.Models.BetType.Regular,
            SubmittedAtUtc = DateTime.UtcNow
        });
        setupContext.SelectionPositions.Add(new SelectionPositionEntity
        {
            SelectionId = existingSelectionId,
            Position = 1,
            DriverId = "OLD"
        });
        await setupContext.SaveChangesAsync();

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
                    CanonicalConflictPolicy = "skip"
                }),
                MigrationExpectedVarianceRuleCatalog.Empty);

            await orchestrator.RunOnceAsync(CancellationToken.None);

            await using var verificationContext = CreateContext();
            var run = await verificationContext.MigrationImportRuns.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).FirstAsync();
            Assert.Equal("Completed", run.Status);

            var diagnostics = await verificationContext.MigrationImportConflictDiagnostics
                .AsNoTracking()
                .Where(x => x.ImportRunId == run.Id)
                .ToListAsync();
            Assert.NotEmpty(diagnostics);
            Assert.Contains(diagnostics, x => x.PolicyOutcome == "Skipped");
            Assert.All(diagnostics, x => Assert.Contains("competitionId:", x.KeyFields, StringComparison.OrdinalIgnoreCase));
            Assert.All(diagnostics, x => Assert.Contains("competitionId:", x.SourceReference, StringComparison.OrdinalIgnoreCase));

            var preservedPosition = await verificationContext.SelectionPositions
                .AsNoTracking()
                .Where(x => x.SelectionId == existingSelectionId)
                .SingleAsync();
            Assert.Equal("OLD", preservedPosition.DriverId);
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
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

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
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

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
    public async Task RunOnceAsync_WhenCanonicalRoundAlreadyExists_ReusesExistingRaceInsteadOfInsertingDuplicateRound()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var competition = new F1.Core.Models.Competition
        {
            Name = "Migration Import 2025",
            Year = 2025,
            Description = "Seeded round collision"
        };
        setupContext.Competitions.Add(competition);
        await setupContext.SaveChangesAsync();

        var existingRaceId = "existing-2025-round-1";
        setupContext.Races.Add(new F1.Core.Models.Race
        {
            Id = existingRaceId,
            CompetitionId = competition.Id,
            Season = 2025,
            Round = 1,
            RaceName = "preexisting_race",
            CircuitName = "preexisting_race",
            StartTimeUtc = DateTime.UtcNow,
            PreQualyDeadlineUtc = DateTime.UtcNow,
            FinalDeadlineUtc = DateTime.UtcNow
        });
        await setupContext.SaveChangesAsync();

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

            Assert.NotNull(await verificationContext.Races.AsNoTracking().FirstOrDefaultAsync(x => x.Id == existingRaceId));

            var firstSelection = await verificationContext.Selections.AsNoTracking().FirstAsync();
            Assert.Equal(existingRaceId, firstSelection.RaceId);
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
        await SeedCanonicalRacesAsync(setupContext, season: 2025);

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
            Assert.NotEmpty(await verificationContext.Races.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.Selections.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.SelectionPositions.AsNoTracking().ToListAsync());
            Assert.Empty(await verificationContext.RacePickScores.AsNoTracking().ToListAsync());
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

    private static async Task SeedCanonicalRacesAsync(F1DbContext context, int season)
    {
        var competition = await context.Competitions
            .FirstOrDefaultAsync(x => x.Year == season && x.Name == $"Migration Import {season}");

        if (competition is null)
        {
            competition = new F1.Core.Models.Competition
            {
                Name = $"Migration Import {season}",
                Year = season,
                Description = "Seeded canonical races for migration write tests"
            };
            context.Competitions.Add(competition);
            await context.SaveChangesAsync();
        }

        var existingRounds = await context.Races
            .Where(x => x.CompetitionId == competition.Id && x.Season == season)
            .Select(x => x.Round)
            .ToListAsync();

        if (existingRounds.Count == 0)
        {
            context.Races.AddRange(
                new F1.Core.Models.Race
                {
                    Id = $"seed-{season}-round-1",
                    CompetitionId = competition.Id,
                    Season = season,
                    Round = 1,
                    RaceName = "Australian Grand Prix",
                    CircuitName = "albert_park",
                    StartTimeUtc = DateTime.UtcNow,
                    PreQualyDeadlineUtc = DateTime.UtcNow,
                    FinalDeadlineUtc = DateTime.UtcNow
                },
                new F1.Core.Models.Race
                {
                    Id = $"seed-{season}-round-2",
                    CompetitionId = competition.Id,
                    Season = season,
                    Round = 2,
                    RaceName = "Chinese Grand Prix",
                    CircuitName = "shanghai",
                    StartTimeUtc = DateTime.UtcNow,
                    PreQualyDeadlineUtc = DateTime.UtcNow,
                    FinalDeadlineUtc = DateTime.UtcNow
                });

            await context.SaveChangesAsync();
        }
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