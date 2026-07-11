using F1.DataSyncWorker.Services;
using F1.Core.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationRaceSelectionParserTests
{
    [Fact]
    public async Task ParseAndPersistAsync_WhenDaveRacesCsvPresent_ParsesRacePicksAndActualsIncludingPQAndBonusFields()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"dave-races-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.RacesFile), "Name");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusFile), "Question");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusAnswersFile), "Question,Answer");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.SideBetsFile), "Race,Bet");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.LeaderboardFile), "Name,Total");

            await using var dbContext = new F1DbContext(options);
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
                    SourceFileName = "races.csv",
                    RowNumber = 1,
                    SectionType = "Header",
                    RawPayload = "Name,Race1-PQ,Race1-1,Race1-2,Race1-3,Race1-DNF,Race1-BQ1,Race1-BQ2"
                },
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    SourceFileName = "races.csv",
                    RowNumber = 2,
                    SectionType = "RacePick",
                    RawPayload = "_Result,,NOR,VER,PIA,None,TSU,HUL"
                },
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    SourceFileName = "races.csv",
                    RowNumber = 3,
                    SectionType = "RacePick",
                    RawPayload = "Alice,Yes,NOR,PIA,VER,None,TSU,HUL"
                });

            await dbContext.SaveChangesAsync();

            var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
            var result = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

            Assert.True(result.SelectionCount >= 14);

            var selections = await dbContext.MigrationImportRaceSelections
                .Where(x => x.ImportRunId == runId)
                .ToListAsync();

            Assert.Contains(selections, x => x.Subject == "Alice" && x.PickType == "PQ" && x.NormalizedValue == "YES");
            Assert.Contains(selections, x => x.Subject == "Alice" && x.PickType == "1" && x.NormalizedValue == "norris");
            Assert.Contains(selections, x => x.Subject == "ACTUAL" && x.PickType == "BQ1" && x.RawValue == "TSU");
            Assert.Contains(selections, x => x.Subject == "ACTUAL" && x.PickType == "DNF");
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
    public async Task ParseAndPersistAsync_WhenDaveBonusFilesPresent_ParsesPreseasonAnswersAndMapsActualsByNormalizedQuestionKey()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"dave-bonus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.RacesFile), "Name");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusFile), "Question");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.BonusAnswersFile), "Question,Answer");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.SideBetsFile), "Race,Bet");
            File.WriteAllText(Path.Combine(tempDirectory, Dave2025SourcePackageContract.LeaderboardFile), "Name,Total");

            await using var dbContext = new F1DbContext(options);
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
                    SourceFileName = "bonus.csv",
                    RowNumber = 1,
                    SectionType = "Header",
                    RawPayload = "Question,Alice,Bob"
                },
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    SourceFileName = "bonus.csv",
                    RowNumber = 2,
                    SectionType = "SeasonQuestionPrediction",
                    RawPayload = "Will rain happen?,Yes,No"
                },
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    SourceFileName = "bonus.csv",
                    RowNumber = 3,
                    SectionType = "SeasonQuestionPrediction",
                    RawPayload = "WDC - 1st?,NOR,VER"
                },
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    SourceFileName = "bonusAnswers.csv",
                    RowNumber = 1,
                    SectionType = "Header",
                    RawPayload = "Question,Answer,Notes"
                },
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    SourceFileName = "bonusAnswers.csv",
                    RowNumber = 2,
                    SectionType = "SeasonQuestionPrediction",
                    RawPayload = "Will rain happen,Yes,matched"
                });

            await dbContext.SaveChangesAsync();

            var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
            var result = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

            Assert.Equal(6, result.PreseasonAnswerCount);

            var preseasonAnswers = await dbContext.MigrationImportPreseasonAnswers
                .Where(x => x.ImportRunId == runId)
                .OrderBy(x => x.RowNumber)
                .ThenBy(x => x.Subject)
                .ToListAsync();

            Assert.Equal(6, preseasonAnswers.Count);
            Assert.Contains(preseasonAnswers, x => x.QuestionKey == "PRE-001" && x.Subject == "ACTUAL" && x.NormalizedAnswer == "YES");
            Assert.Contains(preseasonAnswers, x => x.QuestionKey == "PRE-002" && x.Subject == "ACTUAL" && x.NormalizedAnswer == null);

            var unresolved = await dbContext.MigrationImportUnresolvedTokens
                .Where(x => x.ImportRunId == runId)
                .ToListAsync();
            Assert.Contains(unresolved, x => x.PickType == "QUESTION_KEY" && x.RawToken.Contains("No matching actual answer", StringComparison.OrdinalIgnoreCase));
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
    public async Task ParseAndPersistAsync_WhenSeasonQuestionIsH2h_PersistsGenericH2hTemplateAnswersAndActual()
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

        dbContext.MigrationImportRawRows.AddRange(
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                SectionType = "Header",
                RawPayload = "Question,Philip,Andy,,"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "Head to head: HAM vs VER,HAM,VER,VER"
            });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var result = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(0, result.PreseasonAnswerCount);

        var preseasonAnswers = await dbContext.MigrationImportPreseasonAnswers
            .Where(x => x.ImportRunId == runId)
            .ToListAsync();
        Assert.Empty(preseasonAnswers);

        var template = await dbContext.QuestionTemplates
            .SingleAsync(x => x.QuestionId == "H2H-002");

        Assert.Equal(QuestionCategory.H2H, template.Category);
        Assert.Equal("Head to head: HAM vs VER", template.Prompt);

        var optionsModel = JsonSerializer.Deserialize<H2hQuestionTemplateOptions>(template.OptionsJson!);
        Assert.NotNull(optionsModel);
        Assert.Equal("hamilton", optionsModel!.LeftDriverId);
        Assert.Equal("max_verstappen", optionsModel.RightDriverId);
        Assert.Equal(1, optionsModel.PointsForCorrectPick);

        var answers = await dbContext.QuestionAnswers
            .OrderBy(x => x.ParticipantId)
            .ToListAsync();
        Assert.Equal(2, answers.Count);
        Assert.Equal("hamilton", answers.Single(x => x.ParticipantId == "Philip").ImportedAnswer);
        Assert.Equal("max_verstappen", answers.Single(x => x.ParticipantId == "Andy").ImportedAnswer);

        var actual = await dbContext.QuestionActuals
            .SingleAsync();
        Assert.Equal("max_verstappen", actual.ImportedAnswer);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenPreseasonQuestionRowsExist_PersistsParticipantAndActualAnswersWithRowTraceability()
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
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "At least one driver will win 4 consecutive races?,Y,NONE,NOT,  ,N,Y,Y,Y,N,Y,N,20"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 3,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "WDC - 1st?,NOR,VER,VER,LEC,PIA,NOR,VER,NOR,VER,NOR,NOR/VER;PIA"
            });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        var preseasonAnswers = await dbContext.MigrationImportPreseasonAnswers
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.RowNumber)
            .ThenBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal((MigrationPhil2025CsvContractPolicy.ParticipantColumns.Length + 1) * 2, preseasonAnswers.Count);

        var philipRow2 = preseasonAnswers.Single(x => x.RowNumber == 2 && x.Subject == "Philip" && !x.IsActualOutcome);
        Assert.Equal("PRE-002", philipRow2.QuestionKey);
        Assert.Equal("At least one driver will win 4 consecutive races?", philipRow2.QuestionText);
        Assert.Equal("YES", philipRow2.NormalizedAnswer);

        var andyRow2 = preseasonAnswers.Single(x => x.RowNumber == 2 && x.Subject == "Andy" && !x.IsActualOutcome);
        Assert.Null(andyRow2.NormalizedAnswer);

        var claireRow2 = preseasonAnswers.Single(x => x.RowNumber == 2 && x.Subject == "Claire" && !x.IsActualOutcome);
        Assert.Null(claireRow2.NormalizedAnswer);

        var daveRow2 = preseasonAnswers.Single(x => x.RowNumber == 2 && x.Subject == "Dave" && !x.IsActualOutcome);
        Assert.Equal("NO", daveRow2.NormalizedAnswer);

        var actualRow2 = preseasonAnswers.Single(x => x.RowNumber == 2 && x.Subject == "ACTUAL" && x.IsActualOutcome);
        Assert.Equal("NO", actualRow2.NormalizedAnswer);

        var actualRow3 = preseasonAnswers.Single(x => x.RowNumber == 3 && x.Subject == "ACTUAL" && x.IsActualOutcome);
        Assert.Equal("norris | max_verstappen | piastri", actualRow3.NormalizedAnswer);

        var questionTemplates = await dbContext.QuestionTemplates
            .OrderBy(x => x.QuestionId)
            .ToListAsync();
        Assert.Equal(new[] { "PRE-002", "PRE-003" }, questionTemplates.Select(x => x.QuestionId).ToArray());

        var genericAnswers = await dbContext.QuestionAnswers
            .OrderBy(x => x.QuestionTemplateId)
            .ThenBy(x => x.ParticipantId)
            .ToListAsync();
        Assert.Equal(MigrationPhil2025CsvContractPolicy.ParticipantColumns.Length * 2, genericAnswers.Count);

        var genericPhilipRow2 = genericAnswers.Single(x => x.ParticipantId == "Philip" && x.ImportedAnswer == "YES");
        Assert.Equal("YES", genericPhilipRow2.ImportedAnswer);

        var genericActuals = await dbContext.QuestionActuals
            .OrderBy(x => x.QuestionTemplateId)
            .ToListAsync();
        Assert.Equal(2, genericActuals.Count);
        Assert.Contains(genericActuals, x => x.ImportedAnswer == "norris | max_verstappen | piastri");
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenPhilPreseasonPromptContainsBonus_ClassifiesAsPreseason()
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

        dbContext.MigrationImportRawRows.Add(new MigrationImportRawRowEntity
        {
            ImportRunId = runId,
            RowNumber = 10,
            SectionType = "SeasonQuestionPrediction",
            RawPayload = "On at least one occasion teammates earn bonus points together?,Y,N,Y,Y,N,Y,Y,Y,N,Y,N"
        });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var result = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.True(result.PreseasonAnswerCount > 0);

        var template = await dbContext.QuestionTemplates.SingleAsync();
        Assert.Equal("PRE-010", template.QuestionId);
        Assert.Equal(QuestionCategory.Preseason, template.Category);

        var preseasonAnswers = await dbContext.MigrationImportPreseasonAnswers
            .Where(x => x.ImportRunId == runId && x.QuestionKey == "PRE-010")
            .ToListAsync();
        Assert.NotEmpty(preseasonAnswers);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenPhilContractAndMultipleSeasonCompetitions_UsesPhilipCompetitionForGenericQuestions()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.Competitions.AddRange(
            new Competition { Id = 1, Name = "Main Competition", Year = 2025, Description = "Default seeded competition" },
            new Competition { Id = 2, Name = "Philip 2025", Year = 2025, Description = "Philip 2025 season competition" },
            new Competition { Id = 3, Name = "David 2025", Year = 2025, Description = "David 2025 season competition" });

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
                RowNumber = 2,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "At least one driver will win 4 consecutive races?,Y,NONE,NOT,  ,N,Y,Y,Y,N,Y,N,20"
            },
            new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = 3,
                SectionType = "SeasonQuestionPrediction",
                RawPayload = "WDC - 1st?,NOR,VER,VER,LEC,PIA,NOR,VER,NOR,VER,NOR,NOR/VER;PIA"
            });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        var questionTemplates = await dbContext.QuestionTemplates
            .OrderBy(x => x.QuestionId)
            .ToListAsync();

        Assert.Equal(2, questionTemplates.Count);
        Assert.All(questionTemplates, template => Assert.Equal(2, template.CompetitionId));
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenPreseasonAnswersContainMalformedTokens_PreservesNormalizedTrimmedValue()
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

        dbContext.MigrationImportRawRows.Add(new MigrationImportRawRowEntity
        {
            ImportRunId = runId,
            RowNumber = 2,
            SectionType = "SeasonQuestionPrediction",
            RawPayload = "Doohan gets booted after 6 or less races?,@@@,Y,Y,Y,Y,Y,Y,Y,Y,Y,Y"
        });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        var malformed = await dbContext.MigrationImportPreseasonAnswers
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "Philip");

        Assert.Equal("@@@", malformed.RawAnswer);
        Assert.Equal("@@@", malformed.NormalizedAnswer);

        var genericActual = await dbContext.QuestionActuals.SingleAsync();
        Assert.Equal("YES", genericActual.ImportedAnswer);

        var genericPhilip = await dbContext.QuestionAnswers
            .SingleAsync(x => x.ParticipantId == "Philip");
        Assert.Equal("@@@", genericPhilip.ImportedAnswer);
    }

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
        Assert.Equal("albert_park", ausWinner.RaceCode);
        Assert.Equal("1", ausWinner.PickType);
        Assert.Equal("max_verstappen", ausWinner.NormalizedValue);

        var dnfPhilip = selections.Single(x => x.RowNumber == 3 && x.Subject == "Philip" && !x.IsActualOutcome);
        Assert.Null(dnfPhilip.NormalizedValue);

        var dnfAndy = selections.Single(x => x.RowNumber == 3 && x.Subject == "Andy" && !x.IsActualOutcome);
        Assert.Null(dnfAndy.NormalizedValue);

        var dnfActual = selections.Single(x => x.RowNumber == 3 && x.Subject == "ACTUAL" && x.IsActualOutcome);
        Assert.Equal("sainz doohan", dnfActual.NormalizedValue);
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

        Assert.Equal("albert_park", lRowActual.RaceCode);
        Assert.Equal("2", lRowActual.PickType);
        Assert.Equal("norris", lRowActual.NormalizedValue);
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,Andy,BINGPT,Kevin,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "AUS-1, max , hulk ,   Bear   Man  ,bear,not" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(5, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var selections = await dbContext.MigrationImportRaceSelections
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.Subject)
            .ToListAsync();

        Assert.Equal("hulkenberg", selections.Single(x => x.Subject == "Andy").NormalizedValue);
        Assert.Equal("bearman", selections.Single(x => x.Subject == "BINGPT").NormalizedValue);
        Assert.Equal("bearman", selections.Single(x => x.Subject == "Kevin").NormalizedValue);
        Assert.Equal("max_verstappen", selections.Single(x => x.Subject == "Philip").NormalizedValue);
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
            Assert.Equal("albert_park", token.RaceCode);
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

        var dnfKevin = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 3 && x.Subject == "Kevin" && x.PickType == "DNF");
        Assert.Equal("bortoleto", dnfKevin.NormalizedValue);

        var dnfVeronica = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 3 && x.Subject == "Veronica" && x.PickType == "DNF");
        Assert.Null(dnfVeronica.NormalizedValue);

        var dnfActual = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 3 && x.Subject == "ACTUAL" && x.PickType == "DNF");
        Assert.Equal("sainz doohan bortoleto lawson alonso hadjar", dnfActual.NormalizedValue);

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

        Assert.Equal("leclerc", participantPick.NormalizedValue);
        Assert.Empty(await dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId).ToListAsync());
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenLongRaceLabelsProvided_MapsMonzaAndAustriaToJolpicaCircuitIds()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "MONZA-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePick", RawPayload = "AUSTRIA-2,NOR,NOR" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(4, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var monzaPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "Philip");
        Assert.Equal("monza", monzaPick.RaceCode);

        var austriaPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 3 && x.Subject == "Philip");
        Assert.Equal("red_bull_ring", austriaPick.RaceCode);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenMultiWordRaceLabelsProvided_MapsToExpectedCircuitIds()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "ABU DHABI-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePick", RawPayload = "UNITED STATES-2,NOR,NOR" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(4, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var abuDhabiPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "Philip");
        Assert.Equal("yas_marina", abuDhabiPick.RaceCode);

        var unitedStatesPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 3 && x.Subject == "Philip");
        Assert.Equal("americas", unitedStatesPick.RaceCode);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenSeasonRaceCodesUsed_MapsAllToJolpicaCircuitIds()
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

        var raceLabels = new (string Label, string ExpectedCircuitId)[]
        {
            ("AUS-1", "albert_park"),
            ("CHN-1", "shanghai"),
            ("JPN-1", "suzuka"),
            ("BAH-1", "bahrain"),
            ("SAR-1", "jeddah"),
            ("MIA-1", "miami"),
            ("IMO-1", "imola"),
            ("MON-1", "monaco"),
            ("BAR-1", "catalunya"),
            ("CAN-1", "villeneuve"),
            ("AUT-1", "red_bull_ring"),
            ("GBR-1", "silverstone"),
            ("SPA-1", "spa"),
            ("HUN-1", "hungaroring"),
            ("NED-1", "zandvoort"),
            ("BAK-1", "baku"),
            ("SIN-1", "marina_bay"),
            ("COTA-1", "americas"),
            ("MEX-1", "rodriguez"),
            ("BRA-1", "interlagos"),
            ("LAS-1", "vegas"),
            ("QAT-1", "losail"),
            ("ABD-1", "yas_marina")
        };

        var rows = new List<MigrationImportRawRowEntity>
        {
            new() { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,," }
        };

        for (var index = 0; index < raceLabels.Length; index++)
        {
            rows.Add(new MigrationImportRawRowEntity
            {
                ImportRunId = runId,
                RowNumber = index + 2,
                SectionType = "RacePick",
                RawPayload = $"{raceLabels[index].Label},VER,VER"
            });
        }

        dbContext.MigrationImportRawRows.AddRange(rows);
        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(raceLabels.Length * 2, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        for (var index = 0; index < raceLabels.Length; index++)
        {
            var rowNumber = index + 2;
            var selection = await dbContext.MigrationImportRaceSelections
                .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == rowNumber && x.Subject == "Philip");

            Assert.Equal(raceLabels[index].ExpectedCircuitId, selection.RaceCode);
        }
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenPhilContractUsesSecondAusBlock_MapsToAustriaBySequence()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "AUS-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 3, SectionType = "RacePick", RawPayload = "CHN-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 4, SectionType = "RacePick", RawPayload = "JPN-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 5, SectionType = "RacePick", RawPayload = "BAH-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 6, SectionType = "RacePick", RawPayload = "SAR-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 7, SectionType = "RacePick", RawPayload = "MIA-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 8, SectionType = "RacePick", RawPayload = "IMO-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 9, SectionType = "RacePick", RawPayload = "MON-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 10, SectionType = "RacePick", RawPayload = "BAR-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 11, SectionType = "RacePick", RawPayload = "CAN-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 12, SectionType = "RacePick", RawPayload = "AUS-1,VER,VER" },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 13, SectionType = "RacePick", RawPayload = "AUS-2,NOR,NOR" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(24, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var firstAusPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "Philip");
        Assert.Equal("albert_park", firstAusPick.RaceCode);

        var secondAusWinner = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 12 && x.Subject == "Philip");
        Assert.Equal("red_bull_ring", secondAusWinner.RaceCode);

        var secondAusSecondPlace = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 13 && x.Subject == "Philip");
        Assert.Equal("red_bull_ring", secondAusSecondPlace.RaceCode);
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenPhilContractPodiumContainsNotToken_NormalizesToNor()
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
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Pious,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "HUN-2,NOT,NOR" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(2, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var participantPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "Pious");

        Assert.Equal("norris", participantPick.NormalizedValue);
        Assert.Empty(await dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId).ToListAsync());
    }

    [Fact]
    public async Task ParseAndPersistAsync_WhenNonPhilContractPodiumContainsNotToken_DoesNotNormalizeToNor()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();
        await using var dbContext = new F1DbContext(options);

        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "other.csv",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });

        dbContext.MigrationImportRawRows.AddRange(
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 1, SectionType = "Header", RawPayload = "Question,Philip,," },
            new MigrationImportRawRowEntity { ImportRunId = runId, RowNumber = 2, SectionType = "RacePick", RawPayload = "HUN-2,NOT,NOR" });

        await dbContext.SaveChangesAsync();

        var parser = new MigrationRaceSelectionParser(new TestDbContextFactory(options));
        var parseResult = await parser.ParseAndPersistAsync(runId, CancellationToken.None);

        Assert.Equal(2, parseResult.SelectionCount);
        Assert.Equal(0, parseResult.UnresolvedTokenCount);

        var participantPick = await dbContext.MigrationImportRaceSelections
            .SingleAsync(x => x.ImportRunId == runId && x.RowNumber == 2 && x.Subject == "Philip");

        Assert.Null(participantPick.NormalizedValue);
        Assert.Empty(await dbContext.MigrationImportUnresolvedTokens.Where(x => x.ImportRunId == runId).ToListAsync());
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"m3-parser-{Guid.NewGuid():N}")
            .Options;
    }

    private static void SeedCompetition(F1DbContext dbContext, int competitionId, int year)
    {
        dbContext.Competitions.Add(new Competition
        {
            Id = competitionId,
            Name = $"Competition {competitionId}",
            Year = year,
            Description = "Parser test competition"
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