using F1.DataSyncWorker.Services;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationScoreRecalculatorTests
{
    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPreseasonAnswersPresent_ComputesQuestionScoresAndSeparateTotals()
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

        dbContext.MigrationImportPreseasonPolicies.Add(new MigrationImportPreseasonPolicyEntity
        {
            ImportRunId = runId,
            RowNumber = 2,
            ColumnIndex = 12,
            CellReference = "M2",
            RawPointsPerQuestion = "20",
            PointsPerQuestion = 20
        });

        dbContext.MigrationImportPreseasonAnswers.AddRange(
            // Question 1 matrix: exact, null, mismatch, malformed token mismatch.
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "ACTUAL", "Y", isActual: true),
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "Philip", "Y"),
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "Andy", null),
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "Claire", "N"),
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "Dave", "@@@"),

            // Question 2 with delimited actual answers: multi-token exact matching.
            PreseasonAnswer(runId, 3, "PRE-003", "Q2", "ACTUAL", "NOR | VER | PIA", isActual: true),
            PreseasonAnswer(runId, 3, "PRE-003", "Q2", "Philip", "NOR"),
            PreseasonAnswer(runId, 3, "PRE-003", "Q2", "Andy", "HAM"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var preseasonScores = await dbContext.MigrationImportPreseasonCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(6, preseasonScores.Count);

        AssertPreseasonScore(preseasonScores.Single(x => x.QuestionKey == "PRE-002" && x.Subject == "Philip"), 20, "PRESEASON_EXACT");
        AssertPreseasonScore(preseasonScores.Single(x => x.QuestionKey == "PRE-002" && x.Subject == "Andy"), 0, "PRESEASON_PREDICTION_NULL");
        AssertPreseasonScore(preseasonScores.Single(x => x.QuestionKey == "PRE-002" && x.Subject == "Claire"), 0, "PRESEASON_MISMATCH");
        AssertPreseasonScore(preseasonScores.Single(x => x.QuestionKey == "PRE-002" && x.Subject == "Dave"), 0, "PRESEASON_MISMATCH");
        AssertPreseasonScore(preseasonScores.Single(x => x.QuestionKey == "PRE-003" && x.Subject == "Philip"), 20, "PRESEASON_EXACT");
        AssertPreseasonScore(preseasonScores.Single(x => x.QuestionKey == "PRE-003" && x.Subject == "Andy"), 0, "PRESEASON_MISMATCH");

        var preseasonTotals = await dbContext.MigrationImportPreseasonCalculatedTotals
            .Where(x => x.ImportRunId == runId)
            .ToListAsync();

        Assert.Equal(4, preseasonTotals.Count);
        Assert.Equal(40, preseasonTotals.Single(x => x.Subject == "Philip").CalculatedTotalPoints);
        Assert.Equal(0, preseasonTotals.Single(x => x.Subject == "Andy").CalculatedTotalPoints);
        Assert.Equal(0, preseasonTotals.Single(x => x.Subject == "Claire").CalculatedTotalPoints);
        Assert.Equal(0, preseasonTotals.Single(x => x.Subject == "Dave").CalculatedTotalPoints);
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPhilBooleanAnswerMatchesActual_ScoresExactForDavePre002()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 1, 2025);

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
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                RawPayload = "Question,Philip,New Sexy Ayrton,Andy,Claire,Dave,Kevin,Pious ,Shane,Veronica,BINGPT,,"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "At least one driver will win 4 consecutive races?,Y,N,Y,Y,N,Y,Y,Y,N,Y,N,20"
            });

        dbContext.MigrationImportPreseasonPolicies.Add(new MigrationImportPreseasonPolicyEntity
        {
            ImportRunId = runId,
            RowNumber = 2,
            ColumnIndex = 12,
            CellReference = "M2",
            RawPointsPerQuestion = "20",
            PointsPerQuestion = 20
        });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var daveScore = await dbContext.MigrationImportPreseasonCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.QuestionKey == "PRE-002" && x.Subject == "Dave");

        AssertPreseasonScore(daveScore, 20, "PRESEASON_EXACT");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPreseasonPolicyMissing_SetsPolicyMissingReasonAndZeroPoints()
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

        dbContext.MigrationImportPreseasonAnswers.AddRange(
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "ACTUAL", "Y", isActual: true),
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "Philip", "Y"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var score = await dbContext.MigrationImportPreseasonCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip");

        AssertPreseasonScore(score, 0, "PRESEASON_POLICY_MISSING");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPreseasonActualMissing_SetsActualMissingReasonAndZeroPoints()
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

        dbContext.MigrationImportPreseasonPolicies.Add(new MigrationImportPreseasonPolicyEntity
        {
            ImportRunId = runId,
            RowNumber = 2,
            ColumnIndex = 12,
            CellReference = "M2",
            RawPointsPerQuestion = "20",
            PointsPerQuestion = 20
        });

        dbContext.MigrationImportPreseasonAnswers.Add(
            PreseasonAnswer(runId, 2, "PRE-002", "Q1", "Philip", "Y"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var score = await dbContext.MigrationImportPreseasonCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip");

        AssertPreseasonScore(score, 0, "PRESEASON_ACTUAL_MISSING");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPodiumAndDnfMatrixApplied_ComputesExpectedPoints()
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
            // Actual outcomes for AUS.
            Selection(runId, 100, "AUS", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "AUS", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "AUS", "3", "ACTUAL", "LEC", isActual: true),
            Selection(runId, 103, "AUS", "DNF", "ACTUAL", "SAI DOO", isActual: true),

            // Philip: exact P1, top3 wrong-slot P2, podium miss P3, DNF match.
            Selection(runId, 10, "AUS", "1", "Philip", "VER"),
            Selection(runId, 11, "AUS", "2", "Philip", "LEC"),
            Selection(runId, 12, "AUS", "3", "Philip", "HAM"),
            Selection(runId, 13, "AUS", "DNF", "Philip", "DOO"),

            // Andy: blank DNF with actual DNFs should score 0.
            Selection(runId, 20, "AUS", "DNF", "Andy", null));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(5, result.ScoredPickCount);
        Assert.Equal(20, result.TotalPoints);

        var scores = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ToListAsync();

        Assert.Equal(5, scores.Count);

        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "1"), 10, "PODIUM_EXACT");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "2"), 5, "PODIUM_TOP3_WRONG_SLOT");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "3"), 0, "PODIUM_MISS");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "DNF"), 5, "DNF_MATCH");
        AssertScore(scores.Single(x => x.Subject == "Andy" && x.PickType == "DNF"), 0, "DNF_BLANK_HAS_ACTUAL");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenNoActualDnfs_BlankDnfScoresFive()
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
            Selection(runId, 100, "JPN", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "JPN", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "JPN", "3", "ACTUAL", "LEC", isActual: true),
            Selection(runId, 103, "JPN", "DNF", "ACTUAL", null, isActual: true),
            Selection(runId, 10, "JPN", "DNF", "Philip", null));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(1, result.ScoredPickCount);
        Assert.Equal(5, result.TotalPoints);

        var dnfScore = await dbContext.MigrationImportCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip" && x.PickType == "DNF");

        AssertScore(dnfScore, 5, "DNF_BLANK_NO_ACTUAL");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenDnfUsesMappedDriverIds_MatchesExpectedActualTokens()
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
            Selection(runId, 100, "AUS", "1", "ACTUAL", "max_verstappen", isActual: true),
            Selection(runId, 101, "AUS", "2", "ACTUAL", "norris", isActual: true),
            Selection(runId, 102, "AUS", "3", "ACTUAL", "leclerc", isActual: true),
            Selection(runId, 103, "AUS", "DNF", "ACTUAL", "sainz doohan bortoleto", isActual: true),
            Selection(runId, 10, "AUS", "DNF", "Kevin", "bortoleto"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(1, result.ScoredPickCount);
        Assert.Equal(5, result.TotalPoints);

        var dnfScore = await dbContext.MigrationImportCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Kevin" && x.PickType == "DNF");

        AssertScore(dnfScore, 5, "DNF_MATCH");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPreQualyModeYes_AppliesDavePodiumMultipliers()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportRaceSelections.AddRange(
            Selection(runId, 100, "R01", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "R01", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "R01", "3", "ACTUAL", "LEC", isActual: true),
            Selection(runId, 103, "R01", "DNF", "ACTUAL", "DOO", isActual: true),

            Selection(runId, 10, "R01", "PQ", "Philip", "YES"),
            Selection(runId, 11, "R01", "1", "Philip", "VER"),
            Selection(runId, 12, "R01", "2", "Philip", "LEC"),
            Selection(runId, 13, "R01", "3", "Philip", "HAM"),
            Selection(runId, 14, "R01", "DNF", "Philip", "DOO"),

            Selection(runId, 20, "R01", "PQ", "Andy", "POST"),
            Selection(runId, 21, "R01", "1", "Andy", "VER"),
            Selection(runId, 22, "R01", "2", "Andy", "LEC"),
            Selection(runId, 23, "R01", "3", "Andy", "HAM"),
            Selection(runId, 24, "R01", "DNF", "Andy", "DOO"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(10, result.ScoredPickCount);
        Assert.Equal(47.5m, result.TotalPoints);

        var scores = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .ToListAsync();

        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "1"), 15, "PODIUM_EXACT_PQ_YES");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "2"), 7.5m, "PODIUM_TOP3_WRONG_SLOT_PQ_YES");
        AssertScore(scores.Single(x => x.Subject == "Philip" && x.PickType == "DNF"), 5, "DNF_MATCH");

        AssertScore(scores.Single(x => x.Subject == "Andy" && x.PickType == "1"), 10, "PODIUM_EXACT");
        AssertScore(scores.Single(x => x.Subject == "Andy" && x.PickType == "2"), 5, "PODIUM_TOP3_WRONG_SLOT");
        AssertScore(scores.Single(x => x.Subject == "Andy" && x.PickType == "DNF"), 5, "DNF_MATCH");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPreQualyModeAll_AwardsSingleJackpotWhenAllPredictionsMatch()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportRaceSelections.AddRange(
            Selection(runId, 100, "R02", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "R02", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "R02", "3", "ACTUAL", "LEC", isActual: true),
            Selection(runId, 103, "R02", "DNF", "ACTUAL", "DOO", isActual: true),

            Selection(runId, 10, "R02", "PQ", "Kevin", "ALL"),
            Selection(runId, 11, "R02", "1", "Kevin", "VER"),
            Selection(runId, 12, "R02", "2", "Kevin", "NOR"),
            Selection(runId, 13, "R02", "3", "Kevin", "LEC"),
            Selection(runId, 14, "R02", "DNF", "Kevin", "DOO"),

            Selection(runId, 20, "R02", "PQ", "Shane", "ALL"),
            Selection(runId, 21, "R02", "1", "Shane", "VER"),
            Selection(runId, 22, "R02", "2", "Shane", "HAM"),
            Selection(runId, 23, "R02", "3", "Shane", "LEC"),
            Selection(runId, 24, "R02", "DNF", "Shane", "DOO"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(10, result.ScoredPickCount);
        Assert.Equal(100, result.TotalPoints);

        var scores = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .ToListAsync();

        AssertScore(scores.Single(x => x.Subject == "Kevin" && x.PickType == "1"), 100, "ALL_MODE_JACKPOT");
        AssertScore(scores.Single(x => x.Subject == "Kevin" && x.PickType == "2"), 0, "ALL_MODE_JACKPOT_CREDITED_ON_P1");
        AssertScore(scores.Single(x => x.Subject == "Shane" && x.PickType == "1"), 0, "ALL_MODE_NO_JACKPOT");
        AssertScore(scores.Single(x => x.Subject == "Shane" && x.PickType == "DNF"), 0, "ALL_MODE_NO_JACKPOT");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPreQualyModeAll_BonusQuestionsAreScoredIndependently()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportRaceSelections.AddRange(
            Selection(runId, 100, "R03", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "R03", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "R03", "3", "ACTUAL", "LEC", isActual: true),
            Selection(runId, 103, "R03", "DNF", "ACTUAL", "SAI", isActual: true),
            Selection(runId, 104, "R03", "BQ1", "ACTUAL", "ALB", isActual: true),
            Selection(runId, 105, "R03", "BQ2", "ACTUAL", "ALO", isActual: true),

            Selection(runId, 10, "R03", "PQ", "ColmF", "ALL"),
            Selection(runId, 11, "R03", "1", "ColmF", "VER"),
            Selection(runId, 12, "R03", "2", "ColmF", "PIA"),
            Selection(runId, 13, "R03", "3", "ColmF", "NOR"),
            Selection(runId, 14, "R03", "DNF", "ColmF", "COL"),
            Selection(runId, 15, "R03", "BQ1", "ColmF", "ALB"),
            Selection(runId, 16, "R03", "BQ2", "ColmF", "HUL"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(7, result.ScoredPickCount);
        Assert.Equal(5, result.TotalPoints);

        var scores = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .ToListAsync();

        AssertScore(scores.Single(x => x.Subject == "ColmF" && x.PickType == "1"), 0, "ALL_MODE_NO_JACKPOT");
        AssertScore(scores.Single(x => x.Subject == "ColmF" && x.PickType == "BQ1"), 5, "RACE_BONUS_EXACT");
        AssertScore(scores.Single(x => x.Subject == "ColmF" && x.PickType == "BQ2"), 0, "RACE_BONUS_MISS");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenPreQualyModeYesAndBq2Exact_AwardsTwentyPointRaceBonus()
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
            Selection(runId, 100, "americas", "1", "ACTUAL", "VER", isActual: true),
            Selection(runId, 101, "americas", "2", "ACTUAL", "NOR", isActual: true),
            Selection(runId, 102, "americas", "3", "ACTUAL", "PIA", isActual: true),
            Selection(runId, 103, "americas", "DNF", "ACTUAL", "LAW", isActual: true),
            Selection(runId, 104, "americas", "BQ1", "ACTUAL", "HAD", isActual: true),
            Selection(runId, 105, "americas", "BQ2", "ACTUAL", "ALO", isActual: true),

            Selection(runId, 10, "americas", "PQ", "DayaraY", "Yes"),
            Selection(runId, 11, "americas", "1", "DayaraY", "VER"),
            Selection(runId, 12, "americas", "2", "DayaraY", "NOR"),
            Selection(runId, 13, "americas", "3", "DayaraY", "LEC"),
            Selection(runId, 14, "americas", "DNF", "DayaraY", "SAI"),
            Selection(runId, 15, "americas", "BQ1", "DayaraY", "ALB"),
            Selection(runId, 16, "americas", "BQ2", "DayaraY", "ALO"));

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        var result = await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(7, result.ScoredPickCount);
        Assert.Equal(50, result.TotalPoints);

        var scores = await dbContext.MigrationImportCalculatedScores
            .Where(x => x.ImportRunId == runId && x.Subject == "DayaraY")
            .ToListAsync();

        AssertScore(scores.Single(x => x.PickType == "1"), 15, "PODIUM_EXACT_PQ_YES");
        AssertScore(scores.Single(x => x.PickType == "2"), 15, "PODIUM_EXACT_PQ_YES");
        AssertScore(scores.Single(x => x.PickType == "BQ2"), 20, "RACE_BONUS_EXACT");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenGenericPreseasonQuestionsPresent_PersistsQuestionScoresThroughStrategyDispatch()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 1, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "test.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportPreseasonPolicies.Add(new MigrationImportPreseasonPolicyEntity
        {
            ImportRunId = runId,
            RowNumber = 2,
            ColumnIndex = 12,
            CellReference = "M2",
            RawPointsPerQuestion = "20",
            PointsPerQuestion = 20
        });

        dbContext.MigrationImportPreseasonImportedTallies.AddRange(
            new MigrationImportPreseasonImportedTallyEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                QuestionKey = "PRE-002",
                Subject = "Philip",
                RawPoints = "5",
                ImportedPoints = 5
            },
            new MigrationImportPreseasonImportedTallyEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                QuestionKey = "PRE-002",
                Subject = "Andy",
                RawPoints = "0",
                ImportedPoints = 0
            });

        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 101,
            CompetitionId = 1,
            Season = 2025,
            QuestionId = "PRE-002",
            Category = QuestionCategory.Preseason,
            Prompt = "Q1",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 2,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.AddRange(
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 101,
                ParticipantId = "Philip",
                ImportedAnswer = "VER",
                RecordedAtUtc = DateTime.UtcNow
            },
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 101,
                ParticipantId = "Andy",
                ImportedAnswer = "NOR",
                RecordedAtUtc = DateTime.UtcNow
            });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 101,
            ImportedAnswer = "VER",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var questionScores = await dbContext.QuestionScores
            .OrderBy(x => x.ParticipantId)
            .ToListAsync();

        Assert.Equal(2, questionScores.Count);
        Assert.Equal(20, questionScores.Single(x => x.ParticipantId == "Philip").CalculatedPoints);
        Assert.Equal(0, questionScores.Single(x => x.ParticipantId == "Andy").CalculatedPoints);
        Assert.Equal(5, questionScores.Single(x => x.ParticipantId == "Philip").OverrideScore);
        Assert.Equal("PRESEASON_EXACT", questionScores.Single(x => x.ParticipantId == "Philip").OverrideReasonCode);
        Assert.Equal(runId, questionScores.Single(x => x.ParticipantId == "Philip").OverrideSourceRunId);
        Assert.Null(questionScores.Single(x => x.ParticipantId == "Andy").OverrideScore);

        var legacyScore = await dbContext.MigrationImportPreseasonCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.Subject == "Philip");
        AssertPreseasonScore(legacyScore, 20, "PRESEASON_EXACT");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenGenericPreseasonDataIsPartial_PreservesRunDerivedPreseasonScores()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 1, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "test.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportPreseasonPolicies.Add(new MigrationImportPreseasonPolicyEntity
        {
            ImportRunId = runId,
            RowNumber = 10,
            ColumnIndex = 12,
            CellReference = "M10",
            RawPointsPerQuestion = "20",
            PointsPerQuestion = 20
        });

        dbContext.MigrationImportPreseasonAnswers.AddRange(
            PreseasonAnswer(runId, 10, "PRE-010", "Q10", "ACTUAL", "Y", isActual: true),
            PreseasonAnswer(runId, 10, "PRE-010", "Q10", "Dave", "Y"));

        // Seed unrelated generic preseason data to force generic scoring path.
        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 101,
            CompetitionId = 1,
            Season = 2025,
            QuestionId = "PRE-002",
            Category = QuestionCategory.Preseason,
            Prompt = "Q1",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 2,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.Add(new QuestionAnswerEntity
        {
            QuestionTemplateId = 101,
            ParticipantId = "Philip",
            ImportedAnswer = "VER",
            RecordedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 101,
            ImportedAnswer = "VER",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var daveScore = await dbContext.MigrationImportPreseasonCalculatedScores
            .SingleAsync(x => x.ImportRunId == runId && x.QuestionKey == "PRE-010" && x.Subject == "Dave");

        AssertPreseasonScore(daveScore, 20, "PRESEASON_EXACT");
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenDaveRunHasUnrelatedGenericPreseasonData_DoesNotLeakOtherParticipants()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 2, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025-package",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportRaceSelections.AddRange(
            Selection(runId, 10, "R01", "H2H", "ColmF", "VER"),
            Selection(runId, 11, "R01", "H2H", "ACTUAL", "VER", isActual: true));

        dbContext.QuestionTemplates.AddRange(
            new QuestionTemplateEntity
            {
                Id = 501,
                CompetitionId = 2,
                Season = 2025,
                QuestionId = "H2H-R01",
                Category = QuestionCategory.H2H,
                Prompt = "R01 H2H",
                OptionsJson = "{\"LeftDriverId\":\"VER\",\"RightDriverId\":\"HAM\",\"PointsForCorrectPick\":5}",
                Status = QuestionTemplateStatus.Published,
                SortOrder = 11,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            },
            new QuestionTemplateEntity
            {
                Id = 502,
                CompetitionId = 1,
                Season = 2025,
                QuestionId = "PRE-002",
                Category = QuestionCategory.Preseason,
                Prompt = "Q1",
                Status = QuestionTemplateStatus.Published,
                SortOrder = 2,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

        dbContext.QuestionAnswers.AddRange(
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 501,
                ParticipantId = "ColmF",
                ImportedAnswer = "VER",
                RecordedAtUtc = DateTime.UtcNow
            },
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 502,
                ParticipantId = "Andy",
                ImportedAnswer = "YES",
                RecordedAtUtc = DateTime.UtcNow
            });

        dbContext.QuestionActuals.AddRange(
            new QuestionActualEntity
            {
                QuestionTemplateId = 501,
                ImportedAnswer = "VER",
                RecordedAtUtc = DateTime.UtcNow
            },
            new QuestionActualEntity
            {
                QuestionTemplateId = 502,
                ImportedAnswer = "YES",
                RecordedAtUtc = DateTime.UtcNow
            });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var questionScores = await dbContext.QuestionScores
            .OrderBy(x => x.ParticipantId)
            .ToListAsync();

        Assert.Single(questionScores);
        Assert.Equal("ColmF", questionScores[0].ParticipantId);

        var preseasonScores = await dbContext.MigrationImportPreseasonCalculatedScores
            .Where(x => x.ImportRunId == runId)
            .ToListAsync();

        Assert.Empty(preseasonScores);
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenCategoryStrategyMissing_PersistsZeroPointFallbackReason()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 1, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "test.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 201,
            CompetitionId = 1,
            Season = 2025,
            QuestionId = "H2H-001",
            Category = QuestionCategory.H2H,
            Prompt = "Who finishes higher?",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 1,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.Add(new QuestionAnswerEntity
        {
            QuestionTemplateId = 201,
            ParticipantId = "Philip",
            ImportedAnswer = "NOR",
            RecordedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 201,
            ImportedAnswer = "LEC",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(
            new TestDbContextFactory(options),
            new QuestionScoringStrategyRegistry([]));

        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var score = await dbContext.QuestionScores.SingleAsync(x => x.ParticipantId == "Philip");
        Assert.Equal(0, score.CalculatedPoints);
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenH2hQuestionConfigured_ScoresCorrectChosenDriver()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 1, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "test.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 301,
            CompetitionId = 1,
            Season = 2025,
            QuestionId = "H2H-301",
            Category = QuestionCategory.H2H,
            Prompt = "HAM or VER?",
            OptionsJson = "{\"LeftDriverId\":\"HAM\",\"RightDriverId\":\"VER\",\"PointsForCorrectPick\":5}",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 30,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.AddRange(
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 301,
                ParticipantId = "Philip",
                ImportedAnswer = "HAM",
                RecordedAtUtc = DateTime.UtcNow
            },
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 301,
                ParticipantId = "Andy",
                ImportedAnswer = "VER",
                RecordedAtUtc = DateTime.UtcNow
            });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 301,
            ImportedAnswer = "VER",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var scores = await dbContext.QuestionScores
            .OrderBy(x => x.ParticipantId)
            .ToListAsync();

        Assert.Equal(2, scores.Count);
        Assert.Equal(0, scores.Single(x => x.ParticipantId == "Philip").CalculatedPoints);
        Assert.Equal(5, scores.Single(x => x.ParticipantId == "Andy").CalculatedPoints);
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenRaceBonusToleranceModeConfigured_AwardsPointsWithinTolerance()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 2, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 401,
            CompetitionId = 2,
            Season = 2025,
            QuestionId = "PRE-401",
            Category = QuestionCategory.RaceBonus,
            Prompt = "MON race bonus",
            OptionsJson = "{\"Mode\":\"Tolerance\",\"PointsForCorrectPick\":20,\"Tolerance\":1}",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 401,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.AddRange(
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 401,
                ParticipantId = "Philip",
                ImportedAnswer = "10",
                RecordedAtUtc = DateTime.UtcNow
            },
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 401,
                ParticipantId = "Andy",
                ImportedAnswer = "13",
                RecordedAtUtc = DateTime.UtcNow
            });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 401,
            ImportedAnswer = "11",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var scores = await dbContext.QuestionScores
            .Where(x => x.QuestionTemplateId == 401)
            .OrderBy(x => x.ParticipantId)
            .ToListAsync();

        Assert.Equal(2, scores.Count);
        Assert.Equal(20, scores.Single(x => x.ParticipantId == "Philip").CalculatedPoints);
        Assert.Equal(0, scores.Single(x => x.ParticipantId == "Andy").CalculatedPoints);
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenRaceBonusFormulaModeConfigured_UsesMaxMinusGapScoring()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 2, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 402,
            CompetitionId = 2,
            Season = 2025,
            QuestionId = "PRE-402",
            Category = QuestionCategory.RaceBonus,
            Prompt = "SAU gap bonus",
            OptionsJson = "{\"Mode\":\"FormulaMaxMinusGap\",\"PointsForCorrectPick\":20,\"FormulaMaxPoints\":20,\"FormulaPenaltyPerUnit\":1}",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 402,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.AddRange(
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 402,
                ParticipantId = "Philip",
                ImportedAnswer = "11",
                RecordedAtUtc = DateTime.UtcNow
            },
            new QuestionAnswerEntity
            {
                QuestionTemplateId = 402,
                ParticipantId = "Andy",
                ImportedAnswer = "40",
                RecordedAtUtc = DateTime.UtcNow
            });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 402,
            ImportedAnswer = "15",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var scores = await dbContext.QuestionScores
            .Where(x => x.QuestionTemplateId == 402)
            .OrderBy(x => x.ParticipantId)
            .ToListAsync();

        Assert.Equal(2, scores.Count);
        Assert.Equal(16, scores.Single(x => x.ParticipantId == "Philip").CalculatedPoints);
        Assert.Equal(0, scores.Single(x => x.ParticipantId == "Andy").CalculatedPoints);
    }

    [Fact]
    public async Task RecalculateAndPersistAsync_WhenH2hPointsConfiguredFromProfilePolicy_UsesConfiguredPoints()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        SeedCompetition(dbContext, 2, 2025);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
        {
            Id = 403,
            CompetitionId = 2,
            Season = 2025,
            QuestionId = "H2H-403",
            Category = QuestionCategory.H2H,
            Prompt = "HAM or VER?",
            OptionsJson = "{\"LeftDriverId\":\"HAM\",\"RightDriverId\":\"VER\",\"PointsForCorrectPick\":5}",
            Status = QuestionTemplateStatus.Published,
            SortOrder = 403,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionAnswers.Add(new QuestionAnswerEntity
        {
            QuestionTemplateId = 403,
            ParticipantId = "Dave",
            ImportedAnswer = "VER",
            RecordedAtUtc = DateTime.UtcNow
        });

        dbContext.QuestionActuals.Add(new QuestionActualEntity
        {
            QuestionTemplateId = 403,
            ImportedAnswer = "VER",
            RecordedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var recalculator = new MigrationScoreRecalculator(new TestDbContextFactory(options));
        await recalculator.RecalculateAndPersistAsync(runId, CancellationToken.None);

        var score = await dbContext.QuestionScores
            .SingleAsync(x => x.QuestionTemplateId == 403 && x.ParticipantId == "Dave");

        Assert.Equal(5, score.CalculatedPoints);
    }

    private static MigrationImportRaceSelectionEntity Selection(
        Guid runId,
        int rowNumber,
        string raceCode,
        string pickType,
        string subject,
        string? normalizedValue,
        bool isActual = false)
    {
        return new MigrationImportRaceSelectionEntity
        {
            ImportRunId = runId,
            RowNumber = rowNumber,
            RaceCode = raceCode,
            PickType = pickType,
            Subject = subject,
            RawValue = normalizedValue,
            NormalizedValue = normalizedValue,
            IsActualOutcome = isActual
        };
    }

    private static void AssertScore(MigrationImportCalculatedScoreEntity actual, decimal points, string reasonCode)
    {
        Assert.Equal(points, actual.Points);
        Assert.Equal(reasonCode, actual.ReasonCode);
        Assert.True(actual.Points >= 0);
    }

    private static MigrationImportPreseasonAnswerEntity PreseasonAnswer(
        Guid runId,
        int rowNumber,
        string questionKey,
        string questionText,
        string subject,
        string? normalizedAnswer,
        bool isActual = false)
    {
        return new MigrationImportPreseasonAnswerEntity
        {
            ImportRunId = runId,
            RowNumber = rowNumber,
            QuestionKey = questionKey,
            QuestionText = questionText,
            Subject = subject,
            RawAnswer = normalizedAnswer,
            NormalizedAnswer = normalizedAnswer,
            IsActualOutcome = isActual
        };
    }

    private static void AssertPreseasonScore(MigrationImportPreseasonCalculatedScoreEntity actual, int points, string reasonCode)
    {
        Assert.Equal(points, actual.Points);
        Assert.Equal(reasonCode, actual.ReasonCode);
        Assert.True(actual.Points >= 0);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m6-score-{Guid.NewGuid():N}")
            .Options;
    }

    private static void SeedCompetition(F1DbContext dbContext, int competitionId, int year)
    {
        dbContext.Competitions.Add(new Competition
        {
            Id = competitionId,
            Name = $"Competition {competitionId}",
            Year = year,
            Description = "Score test competition"
        });

        dbContext.SaveChanges();
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
