using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationReconciliationServiceTests
{
    [Fact]
    public async Task ReconcileAndPersistAsync_ProducesDeterministicPickDiffOrdering_FromGoldenFile()
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
            SectionType = "Header",
            RawPayload = "Question,Philip,Andy,"
        });

        dbContext.MigrationImportLegacyPickScores.AddRange(
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 3, RaceCode = "AUS", PickType = "DNF", Subject = "Philip", LegacyPoints = 5 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 4, RaceCode = "AUS", PickType = "1", Subject = "Andy", LegacyPoints = 5 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 5, RaceCode = "AUS", PickType = "2", Subject = "Andy", LegacyPoints = 5 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 6, RaceCode = "AUS", PickType = "DNF", Subject = "Andy", LegacyPoints = 5 });

        dbContext.MigrationImportCalculatedScores.AddRange(
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 3, RaceCode = "AUS", PickType = "DNF", Subject = "Philip", Points = 0, ReasonCode = "DNF_MISS" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 4, RaceCode = "AUS", PickType = "1", Subject = "Andy", Points = 5, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 5, RaceCode = "AUS", PickType = "2", Subject = "Andy", Points = 10, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 6, RaceCode = "AUS", PickType = "DNF", Subject = "Andy", Points = 5, ReasonCode = "DNF_MATCH" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 7, RaceCode = "BHR", PickType = "1", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" });

        await dbContext.SaveChangesAsync();

        var service = new MigrationReconciliationService(new TestDbContextFactory(options));
        var result = await service.ReconcileAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(7, result.PickDiffCount);
        Assert.Equal(3, result.RaceDiffCount);
        Assert.Equal(2, result.ParticipantSummaryCount);
        Assert.Equal(3, result.ReasonSummaryCount);
        Assert.Equal(5, result.TotalDelta);

        var pickDiffs = await dbContext.MigrationImportPickDiffs
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RaceCode)
            .ThenBy(x => x.Subject)
            .ThenBy(x => x.PickType)
            .ToListAsync();

        var actualLines = pickDiffs
            .Select(x => string.Join('|',
                x.RaceCode,
                x.Subject,
                x.PickType,
                x.ImportedPoints?.ToString() ?? string.Empty,
                x.CalculatedPoints?.ToString() ?? string.Empty,
                x.DeltaPoints,
                x.ReasonCode))
            .ToArray();

        var expectedLines = await File.ReadAllLinesAsync(GetGoldenFilePath("MigrationReconciliationExpectedOrder.txt"));
        Assert.Equal(expectedLines, actualLines);

        var nonZero = pickDiffs.Where(x => x.DeltaPoints != 0).ToList();
        Assert.NotEmpty(nonZero);
        Assert.All(nonZero, x => Assert.False(string.IsNullOrWhiteSpace(x.Explanation)));

        var philipFirstPick = pickDiffs.Single(x => x.RaceCode == "AUS" && x.Subject == "Philip" && x.PickType == "1");
        Assert.Contains("race-points row 1, column B", philipFirstPick.Explanation);
        Assert.Contains("race-picks row 1, column B", philipFirstPick.Explanation);

        var philipSecondPick = pickDiffs.Single(x => x.RaceCode == "AUS" && x.Subject == "Philip" && x.PickType == "2");
        Assert.Contains("race-points row 2, column B", philipSecondPick.Explanation);
        Assert.Contains("race-picks row 2, column B", philipSecondPick.Explanation);

        var andySecondPick = pickDiffs.Single(x => x.RaceCode == "AUS" && x.Subject == "Andy" && x.PickType == "2");
        Assert.Contains("race-points row 5, column C", andySecondPick.Explanation);
        Assert.Contains("race-picks row 5, column C", andySecondPick.Explanation);

        var philipAusRaceDiff = await dbContext.MigrationImportRaceDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "AUS" && x.Subject == "Philip");
        Assert.Contains("Contributors:", philipAusRaceDiff.Explanation);
        Assert.Contains("AUS-1 10->5 (-5)", philipAusRaceDiff.Explanation);
        Assert.Contains("imported race-points row 1, column B", philipAusRaceDiff.Explanation);
        Assert.Contains("calculated race-picks row 1, column B", philipAusRaceDiff.Explanation);
        Assert.Contains("AUS-DNF 5->0 (-5)", philipAusRaceDiff.Explanation);

        var andyAusRaceDiff = await dbContext.MigrationImportRaceDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "AUS" && x.Subject == "Andy");
        Assert.Contains("AUS-2 5->10 (5)", andyAusRaceDiff.Explanation);
    }

    [Fact]
    public async Task ReconcileAndPersistAsync_WhenRuleMatches_MarksExpectedVarianceWithoutChangingAmounts()
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
            SectionType = "Header",
            RawPayload = "Question,Philip,Andy,"
        });

        dbContext.MigrationImportLegacyPickScores.AddRange(
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "BHR", PickType = "1", Subject = "Andy", LegacyPoints = 5 });

        dbContext.MigrationImportCalculatedScores.AddRange(
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "BHR", PickType = "1", Subject = "Andy", Points = 10, ReasonCode = "PODIUM_EXACT" });

        await dbContext.SaveChangesAsync();

        var catalog = new TestExpectedVarianceRuleCatalog(
            new MigrationExpectedVarianceRule(
                RuleId: "phil-aus-1-expected",
                ReasonCode: "KNOWN_LEGACY_POINTS_ERROR",
                Subject: "Philip",
                RaceCode: "AUS",
                PickType: "1",
                ImportedSourcePattern: "race-points row 1, column B",
                CalculatedSourcePattern: "race-picks row 1, column B"));

        var service = new MigrationReconciliationService(new TestDbContextFactory(options), catalog);
        await service.ReconcileAndPersistAsync(runId, CancellationToken.None);

        var expectedPick = await dbContext.MigrationImportPickDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "AUS" && x.Subject == "Philip" && x.PickType == "1");

        Assert.True(expectedPick.IsExpectedVariance);
        Assert.Equal("KNOWN_LEGACY_POINTS_ERROR", expectedPick.ExpectedVarianceReasonCode);
        Assert.Equal("phil-aus-1-expected", expectedPick.ExpectedVarianceRuleId);
        Assert.Equal(10, expectedPick.ImportedPoints);
        Assert.Equal(5, expectedPick.CalculatedPoints);
        Assert.Equal(-5, expectedPick.DeltaPoints);

        var unexpectedPick = await dbContext.MigrationImportPickDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "BHR" && x.Subject == "Andy" && x.PickType == "1");

        Assert.False(unexpectedPick.IsExpectedVariance);
        Assert.Null(unexpectedPick.ExpectedVarianceReasonCode);
        Assert.Null(unexpectedPick.ExpectedVarianceRuleId);

        var expectedRace = await dbContext.MigrationImportRaceDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "AUS" && x.Subject == "Philip");

        Assert.True(expectedRace.IsExpectedVariance);
        Assert.Equal("KNOWN_LEGACY_POINTS_ERROR", expectedRace.ExpectedVarianceReasonCode);
        Assert.Equal("phil-aus-1-expected", expectedRace.ExpectedVarianceRuleId);
    }

    [Fact]
    public async Task ReconcileAndPersistAsync_PersistsParticipantAndReasonSummaries()
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
            SectionType = "Header",
            RawPayload = "Question,Philip,Andy,"
        });

        dbContext.MigrationImportLegacyPickScores.AddRange(
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", LegacyPoints = 10 });

        dbContext.MigrationImportCalculatedScores.AddRange(
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 1, RaceCode = "AUS", PickType = "1", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 2, RaceCode = "AUS", PickType = "2", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" });

        await dbContext.SaveChangesAsync();

        var service = new MigrationReconciliationService(new TestDbContextFactory(options));
        await service.ReconcileAndPersistAsync(runId, CancellationToken.None);

        var participantSummary = await dbContext.MigrationImportParticipantDeltaSummaries
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip");

        Assert.Equal(20, participantSummary.ImportedTotalPoints);
        Assert.Equal(15, participantSummary.CalculatedTotalPoints);
        Assert.Equal(-5, participantSummary.NetDeltaPoints);
        Assert.Equal("PODIUM_RULE_VARIANCE", participantSummary.TopReasonCode);
        Assert.Equal(1, participantSummary.TopReasonCount);

        var reasonSummary = await dbContext.MigrationImportReasonCategorySummaries
            .SingleAsync(x => x.ImportRunId == runId && x.ReasonCode == "PODIUM_RULE_VARIANCE");

        Assert.Equal(1, reasonSummary.OccurrenceCount);
        Assert.Equal(-5, reasonSummary.TotalDeltaPoints);
    }

    [Fact]
    public async Task ReconcileAndPersistAsync_TruncatesLongPickExplanation_ToPersistableLength()
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
            SectionType = "Header",
            RawPayload = "Question,Philip,"
        });

        var rowNumbers = Enumerable.Range(1, 500).ToArray();
        dbContext.MigrationImportLegacyPickScores.AddRange(
            rowNumbers.Select(rowNumber => new MigrationImportLegacyPickScoreEntity
            {
                ImportRunId = runId,
                RowNumber = rowNumber,
                RaceCode = "AUS",
                PickType = "1",
                Subject = "Philip",
                LegacyPoints = 1
            }));

        dbContext.MigrationImportCalculatedScores.Add(
            new MigrationImportCalculatedScoreEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                RaceCode = "AUS",
                PickType = "1",
                Subject = "Philip",
                Points = 0,
                ReasonCode = "PODIUM_TOP3_WRONG_SLOT"
            });

        await dbContext.SaveChangesAsync();

        var service = new MigrationReconciliationService(new TestDbContextFactory(options));
        await service.ReconcileAndPersistAsync(runId, CancellationToken.None);

        var pickDiff = await dbContext.MigrationImportPickDiffs
            .SingleAsync(x => x.ImportRunId == runId && x.RaceCode == "AUS" && x.Subject == "Philip" && x.PickType == "1");

        Assert.StartsWith("Philip AUS-1 imported 500", pickDiff.Explanation);
        Assert.Equal(1024, pickDiff.Explanation.Length);
        Assert.EndsWith("...", pickDiff.Explanation);
    }

    [Fact]
    public async Task ReconcileAndPersistAsync_OrdersComparisonsByRaceOccurrence()
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
            SectionType = "Header",
            RawPayload = "Question,Philip,"
        });

        // ZZZ race occurs first by row number, even though alphabetically it sorts after AAA.
        dbContext.MigrationImportLegacyPickScores.AddRange(
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 10, RaceCode = "zzz_race", PickType = "1", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 11, RaceCode = "zzz_race", PickType = "2", Subject = "Philip", LegacyPoints = 0 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 20, RaceCode = "aaa_race", PickType = "1", Subject = "Philip", LegacyPoints = 10 },
            new MigrationImportLegacyPickScoreEntity { ImportRunId = runId, RowNumber = 21, RaceCode = "aaa_race", PickType = "2", Subject = "Philip", LegacyPoints = 0 });

        dbContext.MigrationImportCalculatedScores.AddRange(
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 10, RaceCode = "zzz_race", PickType = "1", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 11, RaceCode = "zzz_race", PickType = "2", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 20, RaceCode = "aaa_race", PickType = "1", Subject = "Philip", Points = 5, ReasonCode = "PODIUM_TOP3_WRONG_SLOT" },
            new MigrationImportCalculatedScoreEntity { ImportRunId = runId, RowNumber = 21, RaceCode = "aaa_race", PickType = "2", Subject = "Philip", Points = 10, ReasonCode = "PODIUM_EXACT" });

        await dbContext.SaveChangesAsync();

        var service = new MigrationReconciliationService(new TestDbContextFactory(options));
        await service.ReconcileAndPersistAsync(runId, CancellationToken.None);

        var pickDiffsByInsertOrder = await dbContext.MigrationImportPickDiffs
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        Assert.Equal("zzz_race", pickDiffsByInsertOrder[0].RaceCode);
        Assert.Equal("zzz_race", pickDiffsByInsertOrder[1].RaceCode);
        Assert.Equal("aaa_race", pickDiffsByInsertOrder[2].RaceCode);
        Assert.Equal("aaa_race", pickDiffsByInsertOrder[3].RaceCode);
    }

    private static string GetGoldenFilePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "F1.Infrastructure.Tests.csproj")))
            {
                return Path.Combine(directory.FullName, "Contracts", "Golden", fileName);
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Unable to locate F1.Infrastructure.Tests project root.");
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m8-reconciliation-{Guid.NewGuid():N}")
            .Options;
    }

    private sealed class TestExpectedVarianceRuleCatalog : IMigrationExpectedVarianceRuleCatalog
    {
        public TestExpectedVarianceRuleCatalog(params MigrationExpectedVarianceRule[] rules)
        {
            Rules = rules.ToArray();
        }

        public IReadOnlyList<MigrationExpectedVarianceRule> Rules { get; }
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
