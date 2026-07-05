using System.Text.RegularExpressions;
using F1.Core.Models;
using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace F1.Infrastructure.Tests.Relational;

[Collection(PostgresContractCollection.Name)]
public class DataSyncOrchestratorIdempotencyTests
{
    private readonly PostgresTestContainerFixture _fixture;

    public DataSyncOrchestratorIdempotencyTests(PostgresTestContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunOnceAsync_WhenExecutedTwice_RemainsIdempotentForCompetitionsDriversAndRaces()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var options = Options.Create(new DataSyncOptions
        {
            AutoMigrate = false,
            IntervalMinutes = 0,
            DeadlineMinutesBeforeStart = 30,
            HttpRetryCount = 0,
            HttpRetryDelayMs = 250,
            Competitions =
            [
                new CompetitionSeedDefinition
                {
                    Key = "main-2026",
                    Name = "Main 2026",
                    Year = 2026,
                    Description = "Main 2026 season competition"
                }
            ],
            Seasons =
            [
                new SeasonSeedDefinition
                {
                    Season = 2026,
                    CompetitionKeys = ["main-2026"]
                }
            ]
        });

        var dbFactory = new TestDbContextFactory(_fixture.ConnectionString);

        var orchestrator = new DataSyncOrchestrator(
            NullLogger<DataSyncOrchestrator>.Instance,
            dbFactory,
            new StubJolpicaClient(CreateDefaultRaces(2026)),
            options);

        await orchestrator.RunOnceAsync(CancellationToken.None);
        await orchestrator.RunOnceAsync(CancellationToken.None);

        await using var verificationContext = CreateContext();

        var competitions = await verificationContext.Competitions.AsNoTracking().ToListAsync();
        var drivers = await verificationContext.Drivers.AsNoTracking().ToListAsync();
        var races = await verificationContext.Races.AsNoTracking().ToListAsync();

        Assert.Single(competitions);
        Assert.Equal("Main 2026", competitions[0].Name);

        Assert.Equal(2, drivers.Count);
        Assert.Single(races);

        var race = races[0];
        Assert.Equal("main-2026-1-bahrain-grand-prix", race.Id);
        Assert.Equal(2026, race.Season);
        Assert.Equal(1, race.Round);
        Assert.Equal("Bahrain Grand Prix", race.RaceName);
        Assert.Equal("Bahrain International Circuit", race.CircuitName);
        Assert.Equal(new DateTime(2026, 3, 8, 15, 0, 0, DateTimeKind.Utc), race.StartTimeUtc);
        Assert.Equal(race.StartTimeUtc.AddMinutes(-30), race.PreQualyDeadlineUtc);
        Assert.Equal(race.StartTimeUtc.AddMinutes(-30), race.FinalDeadlineUtc);

        Assert.Equal(competitions[0].Id, race.CompetitionId);
    }

    [Fact]
    public async Task RunOnceAsync_WhenInputsVaryByCaseAndSymbols_GeneratesStableCanonicalRaceIdAndRemainsIdempotent()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var options = Options.Create(new DataSyncOptions
        {
            AutoMigrate = false,
            IntervalMinutes = 0,
            DeadlineMinutesBeforeStart = 30,
            HttpRetryCount = 0,
            HttpRetryDelayMs = 250,
            Competitions =
            [
                new CompetitionSeedDefinition
                {
                    Key = "Main___2026",
                    Name = "Main 2026",
                    Year = 2026,
                    Description = "Main 2026 season competition"
                }
            ],
            Seasons =
            [
                new SeasonSeedDefinition
                {
                    Season = 2026,
                    CompetitionKeys = ["Main___2026"]
                }
            ]
        });

        var firstRunOrchestrator = new DataSyncOrchestrator(
            NullLogger<DataSyncOrchestrator>.Instance,
            new TestDbContextFactory(_fixture.ConnectionString),
            new StubJolpicaClient(
            [
                new JolpicaRaceDto
                {
                    Season = "2026",
                    Round = "1",
                    RaceName = "Bahrain Grand Prix",
                    Date = "2026-03-08",
                    Time = "15:00:00Z",
                    Circuit = new JolpicaCircuitDto { CircuitName = "Bahrain International Circuit" }
                }
            ]),
            options);

        await firstRunOrchestrator.RunOnceAsync(CancellationToken.None);

        await using var firstVerificationContext = CreateContext();
        var firstRace = await firstVerificationContext.Races.AsNoTracking().SingleAsync();

        var secondRunOrchestrator = new DataSyncOrchestrator(
            NullLogger<DataSyncOrchestrator>.Instance,
            new TestDbContextFactory(_fixture.ConnectionString),
            new StubJolpicaClient(
            [
                new JolpicaRaceDto
                {
                    Season = "2026",
                    Round = "1",
                    RaceName = "  BAHRAIN___grand---PRIX!!!  ",
                    Date = "2026-03-08",
                    Time = "15:00:00Z",
                    Circuit = new JolpicaCircuitDto { CircuitName = "Bahrain International Circuit" }
                }
            ]),
            options);

        await secondRunOrchestrator.RunOnceAsync(CancellationToken.None);

        await using var secondVerificationContext = CreateContext();
        var races = await secondVerificationContext.Races.AsNoTracking().ToListAsync();

        Assert.Single(races);
        Assert.Equal("main-2026-1-bahrain-grand-prix", firstRace.Id);
        Assert.Equal("main-2026-1-bahrain-grand-prix", races[0].Id);
        Assert.Matches(new Regex("^(?:[a-z0-9]+(?:-[a-z0-9]+)*)-(?:\\d{4})-(?:[1-9]\\d*)-(?:[a-z0-9]+(?:-[a-z0-9]+)*)$"), races[0].Id);
    }

    [Fact]
    public async Task RunOnceAsync_WhenRaceTimeMissing_SkipsRaceToAvoidIncorrectDeadlines()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var options = CreateDefaultOptions();
        var missingTimeRace = new JolpicaRaceDto
        {
            Season = "2026",
            Round = "1",
            RaceName = "Bahrain Grand Prix",
            Date = "2026-03-08",
            Time = string.Empty,
            Circuit = new JolpicaCircuitDto { CircuitName = "Bahrain International Circuit" }
        };

        var orchestrator = new DataSyncOrchestrator(
            NullLogger<DataSyncOrchestrator>.Instance,
            new TestDbContextFactory(_fixture.ConnectionString),
            new StubJolpicaClient([missingTimeRace]),
            options);

        await orchestrator.RunOnceAsync(CancellationToken.None);

        await using var verificationContext = CreateContext();
        var races = await verificationContext.Races.AsNoTracking().ToListAsync();
        Assert.Empty(races);
    }

    [Fact]
    public async Task RunOnceAsync_WhenGeneratedRaceIdWouldExceedSchemaLimit_TruncatesWithStableHashSuffix()
    {
        await using var setupContext = CreateContext();
        await setupContext.Database.EnsureDeletedAsync();
        await setupContext.Database.EnsureCreatedAsync();

        var options = Options.Create(new DataSyncOptions
        {
            AutoMigrate = false,
            IntervalMinutes = 0,
            DeadlineMinutesBeforeStart = 30,
            HttpRetryCount = 0,
            HttpRetryDelayMs = 250,
            Competitions =
            [
                new CompetitionSeedDefinition
                {
                    Key = "extremely-long-competition-key-for-regression-testing-id-length-constraints-2026-main",
                    Name = "Main 2026",
                    Year = 2026,
                    Description = "Main 2026 season competition"
                }
            ],
            Seasons =
            [
                new SeasonSeedDefinition
                {
                    Season = 2026,
                    CompetitionKeys = ["extremely-long-competition-key-for-regression-testing-id-length-constraints-2026-main"]
                }
            ]
        });

        var longRaceName = new string('a', 180) + " Grand Prix";
        var races = new[]
        {
            new JolpicaRaceDto
            {
                Season = "2026",
                Round = "1",
                RaceName = longRaceName,
                Date = "2026-03-08",
                Time = "15:00:00Z",
                Circuit = new JolpicaCircuitDto { CircuitName = "Bahrain International Circuit" }
            }
        };

        var orchestrator = new DataSyncOrchestrator(
            NullLogger<DataSyncOrchestrator>.Instance,
            new TestDbContextFactory(_fixture.ConnectionString),
            new StubJolpicaClient(races),
            options);

        await orchestrator.RunOnceAsync(CancellationToken.None);

        await using var verificationContext = CreateContext();
        var race = await verificationContext.Races.AsNoTracking().SingleAsync();
        Assert.True(race.Id.Length <= 128);
    }

    private static IOptions<DataSyncOptions> CreateDefaultOptions()
    {
        return Options.Create(new DataSyncOptions
        {
            AutoMigrate = false,
            IntervalMinutes = 0,
            DeadlineMinutesBeforeStart = 30,
            HttpRetryCount = 0,
            HttpRetryDelayMs = 250,
            Competitions =
            [
                new CompetitionSeedDefinition
                {
                    Key = "main-2026",
                    Name = "Main 2026",
                    Year = 2026,
                    Description = "Main 2026 season competition"
                }
            ],
            Seasons =
            [
                new SeasonSeedDefinition
                {
                    Season = 2026,
                    CompetitionKeys = ["main-2026"]
                }
            ]
        });
    }

    private static IReadOnlyList<JolpicaRaceDto> CreateDefaultRaces(int season)
    {
        return
        [
            new JolpicaRaceDto
            {
                Season = season.ToString(),
                Round = "1",
                RaceName = "Bahrain Grand Prix",
                Date = "2026-03-08",
                Time = "15:00:00Z",
                Circuit = new JolpicaCircuitDto
                {
                    CircuitName = "Bahrain International Circuit"
                }
            }
        ];
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

    private sealed class StubJolpicaClient : IJolpicaClient
    {
        private readonly IReadOnlyList<JolpicaRaceDto> _races;

        public StubJolpicaClient(IReadOnlyList<JolpicaRaceDto> races)
        {
            _races = races;
        }

        public Task<IReadOnlyList<JolpicaDriverDto>> GetDriversAsync(int season, int retryCount, int retryDelayMs, CancellationToken cancellationToken)
        {
            IReadOnlyList<JolpicaDriverDto> drivers =
            [
                new JolpicaDriverDto
                {
                    DriverId = "max_verstappen",
                    GivenName = "Max",
                    FamilyName = "Verstappen",
                    Code = "VER",
                    PermanentNumber = "1",
                    Nationality = "Dutch"
                },
                new JolpicaDriverDto
                {
                    DriverId = "lando_norris",
                    GivenName = "Lando",
                    FamilyName = "Norris",
                    Code = "NOR",
                    PermanentNumber = "4",
                    Nationality = "British"
                }
            ];

            return Task.FromResult(drivers);
        }

        public Task<IReadOnlyList<JolpicaRaceDto>> GetRacesAsync(int season, int retryCount, int retryDelayMs, CancellationToken cancellationToken)
        {
            return Task.FromResult(_races);
        }
    }
}
