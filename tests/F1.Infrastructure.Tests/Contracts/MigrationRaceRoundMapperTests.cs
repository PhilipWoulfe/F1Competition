using F1.DataSyncWorker.Clients;
using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Options;
using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationRaceRoundMapperTests
{
    [Fact]
    public async Task MapAndPersistAsync_WhenRaceCodeRepeats_MapsBySequenceAndWritesWarning()
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
            CreateSelection(runId, 10, "AUS", "1", "Philip"),
            CreateSelection(runId, 20, "MON", "1", "Philip"),
            CreateSelection(runId, 30, "MON", "1", "Philip"));

        await dbContext.SaveChangesAsync();

        var mapper = new MigrationRaceRoundMapper(
            new TestDbContextFactory(options),
            new StubJolpicaClient(),
            Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
            Options.Create(new MigrationImportOptions { Season = 2025 }));

        var result = await mapper.MapAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(3, result.SnapshotCount);
        Assert.Equal(3, result.MappingCount);
        Assert.Equal(1, result.WarningCount);

        var mappings = await dbContext.MigrationImportRaceRoundMappings
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceSequence)
            .ToListAsync();

        Assert.Equal(1, mappings[0].Round);
        Assert.Equal(2, mappings[1].Round);
        Assert.Equal(3, mappings[2].Round);
        Assert.Equal("albert_park", mappings[0].MappedCircuitId);
        Assert.Equal("monaco", mappings[1].MappedCircuitId);
        Assert.Equal("monza", mappings[2].MappedCircuitId);
        Assert.Contains("sequence-based mapping applied", mappings[2].Warning ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        var snapshots = await dbContext.MigrationImportJolpicaRaceSnapshots
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Round)
            .ToListAsync();

        Assert.Equal(3, snapshots.Count);
        Assert.Equal("Monaco Grand Prix", snapshots[1].RaceName);
        Assert.Equal("Italian Grand Prix", snapshots[2].RaceName);
    }

    [Fact]
    public async Task MapAndPersistAsync_WhenSourceHasMoreRaceBlocksThanJolpica_AddsUnmappedWarning()
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
            CreateSelection(runId, 10, "AUS", "1", "Philip"),
            CreateSelection(runId, 20, "CHN", "1", "Philip"),
            CreateSelection(runId, 30, "JPN", "1", "Philip"),
            CreateSelection(runId, 40, "BAH", "1", "Philip"));

        await dbContext.SaveChangesAsync();

        var mapper = new MigrationRaceRoundMapper(
            new TestDbContextFactory(options),
            new StubJolpicaClient(),
            Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
            Options.Create(new MigrationImportOptions { Season = 2025 }));

        var result = await mapper.MapAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(1, result.WarningCount);

        var lastMapping = await dbContext.MigrationImportRaceRoundMappings
            .Where(x => x.ImportRunId == runId)
            .OrderByDescending(x => x.RaceSequence)
            .FirstAsync();

        Assert.Null(lastMapping.Round);
        Assert.Contains("No Jolpica race available", lastMapping.Warning ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MapAndPersistAsync_WhenRaceCodesAreSimilar_AusAndAut_MapBySequenceWithoutAmbiguityWarning()
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
            CreateSelection(runId, 10, "AUS", "1", "Philip"),
            CreateSelection(runId, 20, "AUT", "1", "Philip"));

        await dbContext.SaveChangesAsync();

        var mapper = new MigrationRaceRoundMapper(
            new TestDbContextFactory(options),
            new StubJolpicaClient(),
            Options.Create(new DataSyncOptions { HttpRetryCount = 0, HttpRetryDelayMs = 1 }),
            Options.Create(new MigrationImportOptions { Season = 2025 }));

        var result = await mapper.MapAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(2, result.MappingCount);
        Assert.Equal(0, result.WarningCount);

        var mappings = await dbContext.MigrationImportRaceRoundMappings
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceSequence)
            .ToListAsync();

        Assert.Equal("AUS", mappings[0].SourceRaceCode);
        Assert.Equal(1, mappings[0].Round);
        Assert.Equal("albert_park", mappings[0].MappedCircuitId);
        Assert.Equal("AUT", mappings[1].SourceRaceCode);
        Assert.Equal(2, mappings[1].Round);
        Assert.Equal("monaco", mappings[1].MappedCircuitId);
        Assert.All(mappings, x => Assert.True(string.IsNullOrWhiteSpace(x.Warning)));
    }

    private static MigrationImportRaceSelectionEntity CreateSelection(Guid runId, int rowNumber, string raceCode, string pickType, string subject)
    {
        return new MigrationImportRaceSelectionEntity
        {
            ImportRunId = runId,
            RowNumber = rowNumber,
            RaceCode = raceCode,
            PickType = pickType,
            Subject = subject,
            RawValue = "VER",
            NormalizedValue = "VER",
            IsActualOutcome = false
        };
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m4-mapper-{Guid.NewGuid():N}")
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

    private sealed class StubJolpicaClient : IJolpicaClient
    {
        public Task<IReadOnlyList<JolpicaDriverDto>> GetDriversAsync(int season, int retryCount, int retryDelayMs, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<JolpicaDriverDto>>([]);
        }

        public Task<IReadOnlyList<JolpicaRaceDto>> GetRacesAsync(int season, int retryCount, int retryDelayMs, CancellationToken cancellationToken)
        {
            IReadOnlyList<JolpicaRaceDto> races =
            [
                new() { Season = "2025", Round = "1", RaceName = "Australian Grand Prix", Date = "2025-03-16", Time = "05:00:00Z", Circuit = new JolpicaCircuitDto { CircuitId = "albert_park", CircuitName = "Albert Park" } },
                new() { Season = "2025", Round = "2", RaceName = "Monaco Grand Prix", Date = "2025-05-25", Time = "13:00:00Z", Circuit = new JolpicaCircuitDto { CircuitId = "monaco", CircuitName = "Monaco" } },
                new() { Season = "2025", Round = "3", RaceName = "Italian Grand Prix", Date = "2025-09-07", Time = "13:00:00Z", Circuit = new JolpicaCircuitDto { CircuitId = "monza", CircuitName = "Monza" } }
            ];

            return Task.FromResult(races);
        }
    }
}