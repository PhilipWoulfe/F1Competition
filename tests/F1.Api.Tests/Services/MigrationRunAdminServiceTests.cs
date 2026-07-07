using System.Text.Json;
using F1.Api.Dtos;
using F1.Api.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1.Api.Tests.Services;

public sealed class MigrationRunAdminServiceTests
{
    [Fact]
    public async Task KickoffRunAsync_WhenWriteModeAndCanonicalDataExistsWithoutConfirmation_FailsValidation()
    {
        var options = CreateOptions();
        var sourcePath = CreateTempCsv(
            "Question,Philip\n" +
            "AUS-1,VER\n");

        try
        {
            await using (var dbContext = new F1DbContext(options))
            {
                dbContext.Drivers.Add(new F1.Core.Models.Driver { DriverId = "VER", FullName = "Max Verstappen" });
                await dbContext.SaveChangesAsync();
            }

            await using var serviceContext = new F1DbContext(options);
            var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

            var result = await service.KickoffRunAsync(
                new MigrationRunKickoffCommand(sourcePath, "write", "admin@example.com", ConfirmNonEmptyStrategy: false),
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.False(result.Conflict);
            Assert.NotNull(result.Error);
            Assert.Contains("confirmNonEmptyStrategy", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Null(result.Run);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task KickoffRunAsync_ReturnsNonEmptyStrategyPreviewMetadata()
    {
        var options = CreateOptions();
        var sourcePath = CreateTempCsv(
            "Question,Philip,Andy\n" +
            "AUS-1,VER,NOR\n" +
            "AUS-2,PIA,RUS\n" +
            "BHR-1,LEC,HAM\n");

        try
        {
            await using (var dbContext = new F1DbContext(options))
            {
                dbContext.Drivers.Add(new F1.Core.Models.Driver { DriverId = "VER", FullName = "Max Verstappen" });
                dbContext.Races.Add(new F1.Core.Models.Race
                {
                    Id = "race-1",
                    CompetitionId = 1,
                    Season = 2025,
                    Round = 1,
                    RaceName = "Australian Grand Prix",
                    CircuitName = "Albert Park",
                    StartTimeUtc = DateTime.UtcNow,
                    PreQualyDeadlineUtc = DateTime.UtcNow,
                    FinalDeadlineUtc = DateTime.UtcNow
                });
                dbContext.Selections.Add(new F1.Core.Models.Selection
                {
                    Id = Guid.NewGuid(),
                    UserId = "Philip",
                    RaceId = "race-1",
                    BetType = F1.Core.Models.BetType.Regular,
                    SubmittedAtUtc = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync();
            }

            await using var serviceContext = new F1DbContext(options);
            var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

            var result = await service.KickoffRunAsync(
                new MigrationRunKickoffCommand(sourcePath, "write", "admin@example.com", ConfirmNonEmptyStrategy: true),
                CancellationToken.None);

            Assert.True(result.Success);
            Assert.NotNull(result.Run);
            Assert.Equal("merge_upsert_active_records", result.Run!.NonEmptyDbStrategy);
            Assert.True(result.Run.CanonicalDataPresent);
            Assert.Equal(1, result.Run.ExistingDriverCount);
            Assert.Equal(1, result.Run.ExistingRaceCount);
            Assert.Equal(1, result.Run.ExistingSelectionCount);
            Assert.Equal(2, result.Run.EstimatedAffectedRaceCount);
            Assert.Equal(2, result.Run.EstimatedAffectedParticipantCount);
            Assert.Equal(4, result.Run.EstimatedAffectedSelectionCount);
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    [Fact]
    public async Task RollbackRunAsync_DeletesCanonicalScopeAndPersistsAudit()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            var competition = new F1.Core.Models.Competition
            {
                Name = "Migration Import 2025",
                Year = 2025,
                Description = "Rollback scope"
            };
            dbContext.Competitions.Add(competition);
            await dbContext.SaveChangesAsync();

            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = false,
                Status = "Completed",
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                RawRowCount = 2
            });

            dbContext.MigrationImportRaceSelections.Add(new MigrationImportRaceSelectionEntity
            {
                ImportRunId = runId,
                RowNumber = 2,
                RaceCode = "albert_park",
                PickType = "1",
                Subject = "Philip",
                NormalizedValue = "VER"
            });

            dbContext.MigrationImportRaceRoundMappings.Add(new MigrationImportRaceRoundMappingEntity
            {
                ImportRunId = runId,
                RaceSequence = 1,
                SourceRowNumber = 2,
                SourceRaceCode = "AUS-1",
                Season = 2025,
                Round = 1,
                MappedCircuitId = "albert_park",
                MappedRaceName = "Australian Grand Prix"
            });

            dbContext.Races.Add(new F1.Core.Models.Race
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

            dbContext.Races.Add(new F1.Core.Models.Race
            {
                Id = "migration-2024-albert-park",
                CompetitionId = competition.Id,
                Season = 2024,
                Round = 1,
                RaceName = "albert_park",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            });

            var selectionId = Guid.NewGuid();
            dbContext.Selections.Add(new F1.Core.Models.Selection
            {
                Id = selectionId,
                UserId = "Philip",
                RaceId = "migration-2025-albert-park",
                BetType = F1.Core.Models.BetType.Regular,
                SubmittedAtUtc = DateTime.UtcNow
            });

            dbContext.SelectionPositions.Add(new SelectionPositionEntity
            {
                SelectionId = selectionId,
                Position = 1,
                DriverId = "VER"
            });

            var outOfScopeSelectionId = Guid.NewGuid();
            dbContext.Selections.Add(new F1.Core.Models.Selection
            {
                Id = outOfScopeSelectionId,
                UserId = "Alex",
                RaceId = "migration-2024-albert-park",
                BetType = F1.Core.Models.BetType.Regular,
                SubmittedAtUtc = DateTime.UtcNow
            });
            dbContext.SelectionPositions.Add(new SelectionPositionEntity
            {
                SelectionId = outOfScopeSelectionId,
                Position = 1,
                DriverId = "VER"
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var rollback = await service.RollbackRunAsync(
            new MigrationRunRollbackCommand(runId, "admin@example.com", "Bad write run"),
            CancellationToken.None);

        Assert.True(rollback.Success);
        Assert.NotNull(rollback.Rollback);
        Assert.Equal("RolledBack", rollback.Rollback!.Status);

        await using var verificationContext = new F1DbContext(options);
        Assert.Single(verificationContext.Selections);
        Assert.Single(verificationContext.SelectionPositions);
        Assert.Empty(verificationContext.Races.Where(x => x.Id == "migration-2025-albert-park"));
        Assert.NotNull(await verificationContext.Races.FirstOrDefaultAsync(x => x.Id == "migration-2024-albert-park"));

        var audit = Assert.Single(verificationContext.MigrationImportRollbackAudits);
        Assert.Equal("admin@example.com", audit.Actor);
        Assert.Equal("Bad write run", audit.Reason);
        Assert.Equal("Completed", audit.Outcome);
    }

    [Fact]
    public async Task RollbackRunAsync_WhenRunIsInProgress_ReturnsValidationError()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = false,
                Status = "Running",
                StartedAtUtc = DateTime.UtcNow,
                RawRowCount = 1
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var rollback = await service.RollbackRunAsync(
            new MigrationRunRollbackCommand(runId, "admin@example.com", "not allowed in running state"),
            CancellationToken.None);

        Assert.False(rollback.Success);
        Assert.Equal("Only completed or failed runs can be rolled back.", rollback.Error);
        Assert.Null(rollback.Rollback);
    }

    [Fact]
    public async Task GetRunDetailAsync_IncludesRollbackAuditsWhenPresent()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = false,
                Status = "RolledBack",
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                RawRowCount = 2
            });

            dbContext.MigrationImportRollbackAudits.Add(new MigrationImportRollbackAuditEntity
            {
                ImportRunId = runId,
                Actor = "admin@example.com",
                Reason = "compensating canonical write",
                RequestedAtUtc = DateTime.UtcNow,
                AffectedRaceCount = 1,
                AffectedSelectionCount = 2,
                AffectedSelectionPositionCount = 4,
                Outcome = "Completed"
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None, null);
        Assert.NotNull(detail);
        Assert.NotNull(detail!.RollbackAudits);
        var audit = Assert.Single(detail.RollbackAudits!);
        Assert.Equal("admin@example.com", audit.Actor);
        Assert.Equal("Completed", audit.Outcome);
        Assert.Equal(1, audit.AffectedRaceCount);
        Assert.Equal(2, audit.AffectedSelectionCount);
        Assert.Equal(4, audit.AffectedSelectionPositionCount);
    }

    [Fact]
    public async Task GetRunDetailAsync_WhenRunRawRowCountIsZero_UsesStagedRowFallbackCount()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = false,
                Status = "Failed",
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                RawRowCount = 0
            });

            dbContext.MigrationImportRawRows.AddRange(
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    RowNumber = 1,
                    SectionType = "Header",
                    RawPayload = "Question,Philip",
                    CreatedAtUtc = DateTime.UtcNow
                },
                new MigrationImportRawRowEntity
                {
                    ImportRunId = runId,
                    RowNumber = 2,
                    SectionType = "RacePick",
                    RawPayload = "AUS-1,VER",
                    CreatedAtUtc = DateTime.UtcNow
                });

            dbContext.MigrationImportPickDiffs.Add(new MigrationImportPickDiffEntity
            {
                ImportRunId = runId,
                RaceCode = "albert_park",
                PickType = "1",
                Subject = "Philip",
                ImportedPoints = 10,
                CalculatedPoints = 5,
                DeltaPoints = -5,
                ReasonCode = "PODIUM_RULE_VARIANCE",
                Explanation = "fallback-count"
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None, null);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.RawRowCount);
    }

    [Fact]
    public async Task GetRunDetailAsync_IncludesConflictDiagnosticsForAdmins()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = false,
                Status = "Failed",
                StartedAtUtc = DateTime.UtcNow,
                FinishedAtUtc = DateTime.UtcNow,
                RawRowCount = 2
            });

            dbContext.MigrationImportConflictDiagnostics.Add(new MigrationImportConflictDiagnosticEntity
            {
                ImportRunId = runId,
                EntityType = "Selection",
                ConflictType = "existing_active_selection",
                KeyFields = "raceId:migration-2025-albert-park|subject:Philip",
                SourceReference = "row:2|race:albert_park|subject:Philip",
                PolicyOutcome = "Failed",
                RecommendedAction = "Review conflicting canonical rows and rerun with approved policy.",
                CreatedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None, null);
        Assert.NotNull(detail);
        Assert.NotNull(detail!.ConflictDiagnostics);
        Assert.Single(detail.ConflictDiagnostics!);
        Assert.Equal("Selection", detail.ConflictDiagnostics![0].EntityType);
        Assert.Equal("Failed", detail.ConflictDiagnostics[0].PolicyOutcome);
    }

    [Fact]
    public async Task GetRunsAsync_IncludesPreseasonParticipantDelta_InTotalDeltaPoints()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 6, 10, 1, 0, DateTimeKind.Utc),
                RawRowCount = 2
            });

            dbContext.MigrationImportParticipantDeltaSummaries.Add(new MigrationImportParticipantDeltaSummaryEntity
            {
                Id = 1,
                ImportRunId = runId,
                Subject = "Philip",
                ImportedTotalPoints = 50,
                CalculatedTotalPoints = 45,
                NetDeltaPoints = -5,
                TopReasonCode = "RULE_VARIANCE",
                TopReasonCount = 1
            });

            dbContext.MigrationImportPreseasonParticipantDeltaSummaries.Add(new MigrationImportPreseasonParticipantDeltaSummaryEntity
            {
                Id = 1,
                ImportRunId = runId,
                Subject = "Philip",
                ImportedTotalPoints = 20,
                CalculatedTotalPoints = 15,
                NetDeltaPoints = -5,
                TopReasonCode = "PRESEASON_RULE_VARIANCE",
                TopReasonCount = 1
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var result = await service.GetRunsAsync(new MigrationRunListQuery(1, 25, null, null, null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(-10, item.TotalDeltaPoints);
    }

    [Fact]
    public async Task GetRunDetailAsync_And_ExportRunDiffsAsync_PreservePersistedDiffOrder()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 6, 10, 1, 0, DateTimeKind.Utc),
                RawRowCount = 2
            });

            dbContext.MigrationImportPickDiffs.AddRange(
                new MigrationImportPickDiffEntity
                {
                    Id = 10,
                    ImportRunId = runId,
                    RaceCode = "zzz_race",
                    PickType = "1",
                    Subject = "Philip",
                    ImportedPoints = 10,
                    CalculatedPoints = 5,
                    DeltaPoints = -5,
                    ReasonCode = "PODIUM_RULE_VARIANCE",
                    Explanation = "zzz"
                },
                new MigrationImportPickDiffEntity
                {
                    Id = 20,
                    ImportRunId = runId,
                    RaceCode = "aaa_race",
                    PickType = "1",
                    Subject = "Philip",
                    ImportedPoints = 10,
                    CalculatedPoints = 5,
                    DeltaPoints = -5,
                    ReasonCode = "PODIUM_RULE_VARIANCE",
                    Explanation = "aaa"
                });

            dbContext.MigrationImportRaceDiffs.AddRange(
                new MigrationImportRaceDiffEntity
                {
                    Id = 30,
                    ImportRunId = runId,
                    RaceCode = "zzz_race",
                    Subject = "Philip",
                    ImportedPoints = 10,
                    CalculatedPoints = 5,
                    DeltaPoints = -5,
                    ReasonCode = "PODIUM_RULE_VARIANCE",
                    Explanation = "zzz-race"
                },
                new MigrationImportRaceDiffEntity
                {
                    Id = 40,
                    ImportRunId = runId,
                    RaceCode = "aaa_race",
                    Subject = "Philip",
                    ImportedPoints = 10,
                    CalculatedPoints = 5,
                    DeltaPoints = -5,
                    ReasonCode = "PODIUM_RULE_VARIANCE",
                    Explanation = "aaa-race"
                });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None, null);
        Assert.NotNull(detail);
        Assert.Equal(new[] { "zzz_race", "aaa_race" }, detail!.PickDiffs.Select(x => x.RaceCode).ToArray());
        Assert.Equal(new[] { "zzz_race", "aaa_race" }, detail.RaceDiffs.Select(x => x.RaceCode).ToArray());

        var export = await service.ExportRunDiffsAsync(runId, "pick-diffs", "json", "admin@example.com", CancellationToken.None, null);
        Assert.NotNull(export);
        Assert.True(export!.Success);

        var exportedRows = JsonSerializer.Deserialize<AdminMigrationPickDiffDto[]>(
            export.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(exportedRows);
        Assert.Equal(new[] { "zzz_race", "aaa_race" }, exportedRows!.Select(x => x.RaceCode).ToArray());
    }

    [Fact]
    public async Task ExportRunDiffsAsync_WhenPreseasonExportRequested_ReturnsOrderedRows()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 6, 10, 1, 0, DateTimeKind.Utc),
                RawRowCount = 3
            });

            dbContext.MigrationImportPreseasonQuestionDiffs.AddRange(
                new MigrationImportPreseasonQuestionDiffEntity
                {
                    Id = 20,
                    ImportRunId = runId,
                    RowNumber = 23,
                    QuestionKey = "PRE-023",
                    QuestionText = "Q2",
                    Subject = "Zed",
                    ImportedPoints = 20,
                    CalculatedPoints = 0,
                    DeltaPoints = -20,
                    ReasonCode = "PRESEASON_RULE_VARIANCE",
                    Explanation = "z"
                },
                new MigrationImportPreseasonQuestionDiffEntity
                {
                    Id = 10,
                    ImportRunId = runId,
                    RowNumber = 22,
                    QuestionKey = "PRE-022",
                    QuestionText = "Q1",
                    Subject = "Amy",
                    ImportedPoints = 20,
                    CalculatedPoints = 20,
                    DeltaPoints = 0,
                    ReasonCode = "PRESEASON_POINTS_MATCH",
                    Explanation = "a"
                });

            dbContext.MigrationImportPreseasonParticipantDeltaSummaries.AddRange(
                new MigrationImportPreseasonParticipantDeltaSummaryEntity
                {
                    Id = 2,
                    ImportRunId = runId,
                    Subject = "Zed",
                    ImportedTotalPoints = 20,
                    CalculatedTotalPoints = 0,
                    NetDeltaPoints = -20,
                    TopReasonCode = "PRESEASON_RULE_VARIANCE",
                    TopReasonCount = 1
                },
                new MigrationImportPreseasonParticipantDeltaSummaryEntity
                {
                    Id = 1,
                    ImportRunId = runId,
                    Subject = "Amy",
                    ImportedTotalPoints = 20,
                    CalculatedTotalPoints = 20,
                    NetDeltaPoints = 0,
                    TopReasonCode = "PRESEASON_POINTS_MATCH",
                    TopReasonCount = 1
                });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var questionExport = await service.ExportRunDiffsAsync(runId, "preseason-question-diffs", "json", "admin@example.com", CancellationToken.None, null);
        Assert.NotNull(questionExport);
        Assert.True(questionExport!.Success);
        var questionRows = JsonSerializer.Deserialize<AdminMigrationPreseasonQuestionDiffDto[]>(
            questionExport.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(questionRows);
        Assert.Equal(new[] { 22, 23 }, questionRows!.Select(x => x.RowNumber).ToArray());

        var participantExport = await service.ExportRunDiffsAsync(runId, "preseason-participant-diffs", "json", "admin@example.com", CancellationToken.None, null);
        Assert.NotNull(participantExport);
        Assert.True(participantExport!.Success);
        var participantRows = JsonSerializer.Deserialize<AdminMigrationPreseasonParticipantDeltaDto[]>(
            participantExport.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(participantRows);
        Assert.Equal(new[] { "Amy", "Zed" }, participantRows!.Select(x => x.Subject).ToArray());
    }

    [Fact]
    public async Task GetRunDetailAsync_ProjectsExpectedVarianceMetadata()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 6, 10, 1, 0, DateTimeKind.Utc),
                RawRowCount = 1
            });

            dbContext.MigrationImportPickDiffs.Add(new MigrationImportPickDiffEntity
            {
                Id = 10,
                ImportRunId = runId,
                RaceCode = "AUS",
                PickType = "1",
                Subject = "Philip",
                ImportedPoints = 10,
                CalculatedPoints = 5,
                DeltaPoints = -5,
                ReasonCode = "PODIUM_RULE_VARIANCE",
                IsExpectedVariance = true,
                ExpectedVarianceReasonCode = "KNOWN_LEGACY_POINTS_ERROR",
                ExpectedVarianceRuleId = "phil-aus-1-expected",
                Explanation = "expected"
            });

            dbContext.MigrationImportRaceDiffs.Add(new MigrationImportRaceDiffEntity
            {
                Id = 20,
                ImportRunId = runId,
                RaceCode = "AUS",
                Subject = "Philip",
                ImportedPoints = 10,
                CalculatedPoints = 5,
                DeltaPoints = -5,
                ReasonCode = "PODIUM_RULE_VARIANCE",
                IsExpectedVariance = true,
                ExpectedVarianceReasonCode = "KNOWN_LEGACY_POINTS_ERROR",
                ExpectedVarianceRuleId = "phil-aus-1-expected",
                Explanation = "expected-race"
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None, null);

        Assert.NotNull(detail);
        Assert.True(detail!.PickDiffs.Single().IsExpectedVariance);
        Assert.Equal("KNOWN_LEGACY_POINTS_ERROR", detail.PickDiffs.Single().ExpectedVarianceReasonCode);
        Assert.Equal("phil-aus-1-expected", detail.PickDiffs.Single().ExpectedVarianceRuleId);
        Assert.True(detail.RaceDiffs.Single().IsExpectedVariance);
    }

    [Fact]
    public async Task GetRunDetailAsync_WhenUnexpectedStatusRequested_FiltersToUnexpectedAndReportsBothTotals()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 6, 10, 1, 0, DateTimeKind.Utc),
                RawRowCount = 2
            });

            dbContext.MigrationImportPickDiffs.AddRange(
                new MigrationImportPickDiffEntity
                {
                    Id = 10,
                    ImportRunId = runId,
                    RaceCode = "AUS",
                    PickType = "1",
                    Subject = "Philip",
                    ImportedPoints = 10,
                    CalculatedPoints = 5,
                    DeltaPoints = -5,
                    ReasonCode = "PODIUM_RULE_VARIANCE",
                    IsExpectedVariance = true,
                    ExpectedVarianceReasonCode = "KNOWN_LEGACY_POINTS_ERROR",
                    ExpectedVarianceRuleId = "phil-aus-1-expected",
                    Explanation = "expected"
                },
                new MigrationImportPickDiffEntity
                {
                    Id = 20,
                    ImportRunId = runId,
                    RaceCode = "BHR",
                    PickType = "1",
                    Subject = "Philip",
                    ImportedPoints = 3,
                    CalculatedPoints = 8,
                    DeltaPoints = 5,
                    ReasonCode = "RULE_VARIANCE",
                    IsExpectedVariance = false,
                    Explanation = "unexpected"
                });

            dbContext.MigrationImportRaceDiffs.AddRange(
                new MigrationImportRaceDiffEntity
                {
                    Id = 30,
                    ImportRunId = runId,
                    RaceCode = "AUS",
                    Subject = "Philip",
                    ImportedPoints = 10,
                    CalculatedPoints = 5,
                    DeltaPoints = -5,
                    ReasonCode = "PODIUM_RULE_VARIANCE",
                    IsExpectedVariance = true,
                    ExpectedVarianceReasonCode = "KNOWN_LEGACY_POINTS_ERROR",
                    ExpectedVarianceRuleId = "phil-aus-1-expected",
                    Explanation = "expected-race"
                },
                new MigrationImportRaceDiffEntity
                {
                    Id = 40,
                    ImportRunId = runId,
                    RaceCode = "BHR",
                    Subject = "Philip",
                    ImportedPoints = 3,
                    CalculatedPoints = 8,
                    DeltaPoints = 5,
                    ReasonCode = "RULE_VARIANCE",
                    IsExpectedVariance = false,
                    Explanation = "unexpected-race"
                });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None, "unexpected");

        Assert.NotNull(detail);
        Assert.Equal(1, detail!.PickDiffCount);
        Assert.Equal(1, detail.RaceDiffCount);
        Assert.Equal(0, detail.TotalDeltaPoints);
        Assert.Equal(5, detail.UnexpectedTotalDeltaPoints);
        Assert.Single(detail.PickDiffs);
        Assert.Single(detail.RaceDiffs);
        Assert.Equal("BHR", detail.PickDiffs[0].RaceCode);
        Assert.Equal("BHR", detail.RaceDiffs[0].RaceCode);
    }

    [Fact]
    public async Task GetRunDetailAsync_ProjectsPreseasonSections_WithDeterministicOrdering()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 6, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 6, 10, 1, 0, DateTimeKind.Utc),
                RawRowCount = 3
            });

            dbContext.MigrationImportPreseasonQuestionDiffs.AddRange(
                new MigrationImportPreseasonQuestionDiffEntity
                {
                    Id = 20,
                    ImportRunId = runId,
                    RowNumber = 23,
                    QuestionKey = "PRE-023",
                    QuestionText = "Q2",
                    Subject = "Philip",
                    ImportedPoints = 20,
                    CalculatedPoints = 0,
                    DeltaPoints = -20,
                    ReasonCode = "PRESEASON_RULE_VARIANCE",
                    Explanation = "q2"
                },
                new MigrationImportPreseasonQuestionDiffEntity
                {
                    Id = 10,
                    ImportRunId = runId,
                    RowNumber = 22,
                    QuestionKey = "PRE-022",
                    QuestionText = "Q1",
                    Subject = "Andy",
                    ImportedPoints = 20,
                    CalculatedPoints = 0,
                    DeltaPoints = -20,
                    ReasonCode = "PRESEASON_RULE_VARIANCE",
                    Explanation = "q1-andy"
                },
                new MigrationImportPreseasonQuestionDiffEntity
                {
                    Id = 30,
                    ImportRunId = runId,
                    RowNumber = 22,
                    QuestionKey = "PRE-022",
                    QuestionText = "Q1",
                    Subject = "Philip",
                    ImportedPoints = 20,
                    CalculatedPoints = 20,
                    DeltaPoints = 0,
                    ReasonCode = "PRESEASON_POINTS_MATCH",
                    Explanation = "q1-philip"
                });

            dbContext.MigrationImportPreseasonParticipantDeltaSummaries.AddRange(
                new MigrationImportPreseasonParticipantDeltaSummaryEntity
                {
                    Id = 1,
                    ImportRunId = runId,
                    Subject = "Philip",
                    ImportedTotalPoints = 40,
                    CalculatedTotalPoints = 20,
                    NetDeltaPoints = -20,
                    TopReasonCode = "PRESEASON_RULE_VARIANCE",
                    TopReasonCount = 1
                },
                new MigrationImportPreseasonParticipantDeltaSummaryEntity
                {
                    Id = 2,
                    ImportRunId = runId,
                    Subject = "Andy",
                    ImportedTotalPoints = 20,
                    CalculatedTotalPoints = 0,
                    NetDeltaPoints = -20,
                    TopReasonCode = "PRESEASON_RULE_VARIANCE",
                    TopReasonCount = 1
                });

            dbContext.MigrationImportPreseasonReasonCategorySummaries.AddRange(
                new MigrationImportPreseasonReasonCategorySummaryEntity
                {
                    Id = 2,
                    ImportRunId = runId,
                    ReasonCode = "PRESEASON_IMPORTED_MISSING",
                    OccurrenceCount = 1,
                    TotalDeltaPoints = 0
                },
                new MigrationImportPreseasonReasonCategorySummaryEntity
                {
                    Id = 1,
                    ImportRunId = runId,
                    ReasonCode = "PRESEASON_RULE_VARIANCE",
                    OccurrenceCount = 2,
                    TotalDeltaPoints = -40
                });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None, null);

        Assert.NotNull(detail);
        Assert.Equal(3, detail!.PreseasonSummary.QuestionDiffCount);
        Assert.Equal(2, detail.PreseasonSummary.ParticipantDeltaCount);
        Assert.Equal(2, detail.PreseasonSummary.ReasonCategoryCount);
        Assert.Equal(-40, detail.PreseasonSummary.TotalDeltaPoints);
        Assert.Equal(-40, detail.TotalDeltaPoints);

        Assert.Equal(new[] { "Andy", "Philip", "Philip" }, detail.PreseasonQuestionDiffs.Select(x => x.Subject).ToArray());
        Assert.Equal(new[] { 22, 22, 23 }, detail.PreseasonQuestionDiffs.Select(x => x.RowNumber).ToArray());
        Assert.Equal(new[] { "Andy", "Philip" }, detail.PreseasonParticipantDeltas.Select(x => x.Subject).ToArray());
        Assert.Equal(new[] { "PRESEASON_RULE_VARIANCE", "PRESEASON_IMPORTED_MISSING" }, detail.PreseasonReasonCategorySummaries.Select(x => x.ReasonCode).ToArray());
    }

    [Fact]
    public async Task GetQuestionDiffsAsync_AppliesFiltersAndReturnsStablePagination()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 7, 10, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 7, 10, 1, 0, DateTimeKind.Utc),
                RawRowCount = 4
            });

            dbContext.QuestionTemplates.AddRange(
                new QuestionTemplateEntity
                {
                    Id = 1,
                    CompetitionId = 1,
                    Season = 2025,
                    QuestionId = "H2H-001",
                    Category = F1.Core.Models.QuestionCategory.H2H,
                    Prompt = "H2H prompt",
                    Status = F1.Core.Models.QuestionTemplateStatus.Published,
                    SortOrder = 1,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                },
                new QuestionTemplateEntity
                {
                    Id = 2,
                    CompetitionId = 1,
                    Season = 2025,
                    QuestionId = "PRE-022",
                    Category = F1.Core.Models.QuestionCategory.Preseason,
                    Prompt = "Preseason prompt",
                    Status = F1.Core.Models.QuestionTemplateStatus.Published,
                    SortOrder = 2,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });

            dbContext.QuestionScores.AddRange(
                new QuestionScoreEntity
                {
                    Id = 1,
                    QuestionTemplateId = 2,
                    ParticipantId = "Morgan",
                    ImportedPoints = 20,
                    CalculatedPoints = 0,
                    DeltaPoints = -20,
                    ReasonCode = "PRESEASON_RULE_VARIANCE",
                    RecordedAtUtc = DateTime.UtcNow
                },
                new QuestionScoreEntity
                {
                    Id = 2,
                    QuestionTemplateId = 2,
                    ParticipantId = "Taylor",
                    ImportedPoints = 20,
                    CalculatedPoints = 20,
                    DeltaPoints = 0,
                    ReasonCode = "PRESEASON_POINTS_MATCH",
                    RecordedAtUtc = DateTime.UtcNow
                },
                new QuestionScoreEntity
                {
                    Id = 3,
                    QuestionTemplateId = 1,
                    ParticipantId = "Morgan",
                    ImportedPoints = 10,
                    CalculatedPoints = 5,
                    DeltaPoints = -5,
                    ReasonCode = "H2H_RULE_VARIANCE",
                    RecordedAtUtc = DateTime.UtcNow
                });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var page1 = await service.GetQuestionDiffsAsync(
            runId,
            page: 1,
            pageSize: 1,
            requestedBy: "admin@example.com",
            category: "Preseason",
            participant: "mor",
            expectedStatus: "unexpected",
            nonZeroDeltaOnly: true,
            cancellationToken: CancellationToken.None);

        Assert.NotNull(page1);
        Assert.Equal(1, page1!.TotalCount);
        Assert.Single(page1.Items);
        Assert.Equal("Preseason", page1.Items[0].Category);
        Assert.Equal("PRE-022", page1.Items[0].QuestionId);
        Assert.Equal("Morgan", page1.Items[0].Participant);

        var summary = await service.GetQuestionDiffSummaryAsync(
            runId,
            "admin@example.com",
            CancellationToken.None,
            category: "all",
            participant: null,
            expectedStatus: "all",
            nonZeroDeltaOnly: false);

        Assert.NotNull(summary);
        Assert.Equal(3, summary!.TotalCount);
        Assert.Equal(2, summary.NonZeroDeltaCount);
        Assert.Equal(-25, summary.TotalDeltaPoints);
        Assert.Equal(new[] { "H2H", "Preseason" }, summary.Categories.Select(x => x.Category).ToArray());
    }

    [Fact]
    public async Task ExportRunDiffsAsync_WhenQuestionDiffExportRequested_IncludesRequiredColumns()
    {
        var runId = Guid.NewGuid();
        var options = CreateOptions();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "test.csv",
                SourceFileChecksum = "abc",
                IsDryRun = true,
                Status = "Completed",
                StartedAtUtc = new DateTime(2026, 7, 7, 11, 0, 0, DateTimeKind.Utc),
                FinishedAtUtc = new DateTime(2026, 7, 7, 11, 1, 0, DateTimeKind.Utc),
                RawRowCount = 1
            });

            dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
            {
                Id = 11,
                CompetitionId = 1,
                Season = 2025,
                QuestionId = "PRE-001",
                Category = F1.Core.Models.QuestionCategory.Preseason,
                Prompt = "Will Team X win?",
                Status = F1.Core.Models.QuestionTemplateStatus.Published,
                SortOrder = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            dbContext.QuestionScores.Add(new QuestionScoreEntity
            {
                Id = 21,
                QuestionTemplateId = 11,
                ParticipantId = "Philip",
                ImportedPoints = 5,
                CalculatedPoints = 0,
                DeltaPoints = -5,
                ReasonCode = "PRESEASON_RULE_VARIANCE",
                RecordedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = new MigrationRunAdminService(serviceContext, NullLogger<MigrationRunAdminService>.Instance);

        var export = await service.ExportRunDiffsAsync(
            runId,
            "question-diffs",
            "csv",
            "admin@example.com",
            CancellationToken.None,
            expectedStatus: "all",
            category: "Preseason",
            participant: "phil",
            nonZeroDeltaOnly: true);

        Assert.NotNull(export);
        Assert.True(export!.Success);

        var csv = System.Text.Encoding.UTF8.GetString(export.Payload);
        Assert.Contains("category,questionId,questionText,participant,importedPoints,calculatedPoints,deltaPoints,reasonCode", csv, StringComparison.Ordinal);
        Assert.Contains("Preseason,PRE-001,Will Team X win?,Philip,5,0,-5,PRESEASON_RULE_VARIANCE", csv, StringComparison.Ordinal);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"migration-run-admin-service-{Guid.NewGuid():N}")
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    private static string CreateTempCsv(string content)
    {
        var allowedTempRoot = Path.Combine(Path.GetTempPath(), "f1-imports", "tests");
        Directory.CreateDirectory(allowedTempRoot);
        var path = Path.Combine(allowedTempRoot, $"f1-admin-migration-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, content);
        return path;
    }
}