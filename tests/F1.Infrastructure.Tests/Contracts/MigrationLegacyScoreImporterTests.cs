using F1.DataSyncWorker.Services;
using F1.DataSyncWorker.Options;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationLegacyScoreImporterTests
{
    [Fact]
    public async Task ImportAndPersistAsync_WhenPhilPreseasonPolicyAndTalliesPresent_PersistsPolicyAndTallies()
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
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                RawPayload = "Question,Philip,New Sexy Ayrton,Andy,Claire,Dave,Kevin,Pious,Shane,Veronica,BinGPT,,"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "At least one driver will win 4 consecutive races?,Y,N,Y,Y,N,Y,Y,Y,N,Y,N,20"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 22,
                SectionType = "SeasonQuestionPoints",
                RawPayload = "At least one driver will win 4 consecutive races?,0,20,0,0,20,0,0,0,20,0,,"
            });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        var policy = await dbContext.MigrationImportPreseasonPolicies
            .SingleAsync(x => x.ImportRunId == runId);
        Assert.Equal(2, policy.RowNumber);
        Assert.Equal("M2", policy.CellReference);
        Assert.Equal("20", policy.RawPointsPerQuestion);
        Assert.Equal(20, policy.PointsPerQuestion);

        var tallies = await dbContext.MigrationImportPreseasonImportedTallies
            .Where(x => x.ImportRunId == runId && x.RowNumber == 22)
            .OrderBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(10, tallies.Count);
        Assert.Equal("PRE-002", tallies[0].QuestionKey);
        Assert.Contains(tallies, x => x.Subject == "Philip" && x.ImportedPoints == 0);
        Assert.Contains(tallies, x => x.Subject == "New Sexy Ayrton" && x.ImportedPoints == 20);
        Assert.Contains(tallies, x => x.Subject == "Veronica" && x.ImportedPoints == 20);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenPreseasonPolicyMissingAndFailEnabled_Throws()
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
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                RawPayload = "Question,Philip,New Sexy Ayrton,Andy,Claire,Dave,Kevin,Pious,Shane,Veronica,BinGPT,,"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 22,
                SectionType = "SeasonQuestionPoints",
                RawPayload = "At least one driver will win 4 consecutive races?,0,20,0,0,20,0,0,0,20,0,,"
            });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(
            new TestDbContextFactory(options),
            Options.Create(new MigrationImportOptions { FailOnPreseasonPolicyParseError = true }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAndPersistAsync(runId, CancellationToken.None));
        Assert.Contains("Preseason policy parse failed", exception.Message);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenPreseasonPolicyMalformedAndFailDisabled_PersistsRawWithNullParsedValue()
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
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                RawPayload = "Question,Philip,New Sexy Ayrton,Andy,Claire,Dave,Kevin,Pious,Shane,Veronica,BinGPT,,"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "At least one driver will win 4 consecutive races?,Y,N,Y,Y,N,Y,Y,Y,N,Y,N,twenty"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 22,
                SectionType = "SeasonQuestionPoints",
                RawPayload = "At least one driver will win 4 consecutive races?,0,20,0,0,20,0,0,0,20,0,,"
            });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        var policy = await dbContext.MigrationImportPreseasonPolicies
            .SingleAsync(x => x.ImportRunId == runId);

        Assert.Equal("twenty", policy.RawPointsPerQuestion);
        Assert.Null(policy.PointsPerQuestion);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenPreseasonTallyMalformedAndFailEnabled_Throws()
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
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                RawPayload = "Question,Philip,New Sexy Ayrton,Andy,Claire,Dave,Kevin,Pious,Shane,Veronica,BinGPT,,"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "At least one driver will win 4 consecutive races?,Y,N,Y,Y,N,Y,Y,Y,N,Y,N,20"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 22,
                SectionType = "SeasonQuestionPoints",
                RawPayload = "At least one driver will win 4 consecutive races?,N/A,20,0,0,20,0,0,0,20,0,,"
            });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(
            new TestDbContextFactory(options),
            Options.Create(new MigrationImportOptions { FailOnPreseasonTallyParseError = true }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => importer.ImportAndPersistAsync(runId, CancellationToken.None));
        Assert.Contains("Preseason tally parse failed", exception.Message);
    }

    [Fact]
    public async Task ImportAndPersistAsync_WhenPreseasonTallyMalformedAndFailDisabled_PersistsRawWithNullParsedValue()
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
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                RawPayload = "Question,Philip,New Sexy Ayrton,Andy,Claire,Dave,Kevin,Pious,Shane,Veronica,BinGPT,,"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "At least one driver will win 4 consecutive races?,Y,N,Y,Y,N,Y,Y,Y,N,Y,N,20"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 22,
                SectionType = "SeasonQuestionPoints",
                RawPayload = "At least one driver will win 4 consecutive races?,N/A,20,0,0,20,0,0,0,20,0,,"
            });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        var malformedTally = await dbContext.MigrationImportPreseasonImportedTallies
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 22 && x.Subject == "Philip");

        Assert.Equal("N/A", malformedTally.RawPoints);
        Assert.Null(malformedTally.ImportedPoints);
    }

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
            (decimal?)importedTotals.Single(x => x.Subject == "Philip").ImportedTotalPoints,
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
    public async Task ImportAndPersistAsync_WhenDaveLeaderboardRowsPresent_PersistsComponentTotalsAndFinalTotal()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"dave-leaderboard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.RacesFile), "Name,Race1-PQ");
        File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusFile), "Question,Philip");
        File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusAnswersFile), "Question,Answer");
        File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.LeaderboardFile), "Name,Total");

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = tempDirectory,
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
                SourceFileName = Dave2025SourcePackageContract.RacesFile,
                RawPayload = "Name,Race1-PQ"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                SourceFileName = Dave2025SourcePackageContract.BonusFile,
                RawPayload = "Question,Philip"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                SourceFileName = Dave2025SourcePackageContract.BonusAnswersFile,
                RawPayload = "Question,Answer"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Unclassified",
                SourceFileName = Dave2025SourcePackageContract.LeaderboardFile,
                RawPayload = "Name,Race Points,Bonus Points,CDP,Total,Final"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                SectionType = "Unclassified",
                SourceFileName = Dave2025SourcePackageContract.LeaderboardFile,
                RawPayload = "Philip,512.5,60,14,572.5,590.5"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 3,
                SectionType = "Unclassified",
                SourceFileName = Dave2025SourcePackageContract.LeaderboardFile,
                RawPayload = "Andy,480,30,12,510,"
            });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        var result = await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(6, result.LegacyPickScoreCount);
        Assert.Equal(2, result.ImportedTotalCount);
        Assert.Equal(0, result.CalculatedTotalCount);

        var legacy = await dbContext.MigrationImportLegacyPickScores
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Subject)
            .ThenBy(x => x.PickType)
            .ToListAsync();

        Assert.Equal(6, legacy.Count);
        Assert.Contains(legacy, x => x.Subject == "Philip" && x.PickType == "RACE_TOTAL" && x.LegacyPoints == 513);
        Assert.Contains(legacy, x => x.Subject == "Philip" && x.PickType == "BONUS_TOTAL" && x.LegacyPoints == 60);
        Assert.Contains(legacy, x => x.Subject == "Philip" && x.PickType == "CDP" && x.LegacyPoints == 14);
        Assert.Contains(legacy, x => x.Subject == "Andy" && x.PickType == "RACE_TOTAL" && x.LegacyPoints == 480);

        var importedTotals = await dbContext.MigrationImportImportedTotals
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal(2, importedTotals.Count);
        Assert.Equal(591, importedTotals.Single(x => x.Subject == "Philip").ImportedTotalPoints);
        Assert.Equal("590.5", importedTotals.Single(x => x.Subject == "Philip").RawTotal);
        Assert.Equal(510, importedTotals.Single(x => x.Subject == "Andy").ImportedTotalPoints);

        var davePolicy = await dbContext.MigrationImportPreseasonPolicies
            .SingleAsync(x => x.ImportRunId == runId);
        Assert.Equal(30, davePolicy.PointsPerQuestion);
        Assert.Equal("DaveDefault", davePolicy.CellReference);

        Directory.Delete(tempDirectory, recursive: true);
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

    [Fact]
    public async Task ImportAndPersistAsync_WhenPhilContractIncludesBakAndCota_DnfRowsMapToAmericasMexicoBrazilInOrder()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,Andy,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 140, SectionType = "RacePoints", RawPayload = "AUS-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 144, SectionType = "RacePoints", RawPayload = "CHN-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 148, SectionType = "RacePoints", RawPayload = "JPN-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 152, SectionType = "RacePoints", RawPayload = "BAH-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 156, SectionType = "RacePoints", RawPayload = "SAR-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 160, SectionType = "RacePoints", RawPayload = "MIA-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 164, SectionType = "RacePoints", RawPayload = "IMO-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 168, SectionType = "RacePoints", RawPayload = "MON-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 172, SectionType = "RacePoints", RawPayload = "BAR-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 176, SectionType = "RacePoints", RawPayload = "CAN-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 180, SectionType = "RacePoints", RawPayload = "AUS-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 184, SectionType = "RacePoints", RawPayload = "GBR-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 188, SectionType = "RacePoints", RawPayload = "SPA-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 192, SectionType = "RacePoints", RawPayload = "HUN-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 196, SectionType = "RacePoints", RawPayload = "NED-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 200, SectionType = "RacePoints", RawPayload = "MON-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 204, SectionType = "RacePoints", RawPayload = "BAK-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 208, SectionType = "RacePoints", RawPayload = "SIN-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 212, SectionType = "RacePoints", RawPayload = "COTA-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 215, SectionType = "RacePoints", RawPayload = "COTA-DNF,0,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 216, SectionType = "RacePoints", RawPayload = "MEX-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 219, SectionType = "RacePoints", RawPayload = "MEX-DNF,0,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 220, SectionType = "RacePoints", RawPayload = "BRA-1,10,10" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 223, SectionType = "RacePoints", RawPayload = "BRA-DNF,0,0" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 236, SectionType = "TotalsMeta", RawPayload = "Result,230,230" });

        await dbContext.SaveChangesAsync();

        var importer = new MigrationLegacyScoreImporter(new TestDbContextFactory(options));
        await importer.ImportAndPersistAsync(runId, CancellationToken.None);

        var cotaDnf = await dbContext.MigrationImportLegacyPickScores
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 215 && x.PickType == "DNF" && x.Subject == "Andy");
        Assert.Equal("americas", cotaDnf.RaceCode);

        var mexDnf = await dbContext.MigrationImportLegacyPickScores
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 219 && x.PickType == "DNF" && x.Subject == "Andy");
        Assert.Equal("rodriguez", mexDnf.RaceCode);

        var braDnf = await dbContext.MigrationImportLegacyPickScores
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 223 && x.PickType == "DNF" && x.Subject == "Andy");
        Assert.Equal("interlagos", braDnf.RaceCode);
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
