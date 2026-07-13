using F1.Api.Configuration;
using F1.Api.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace F1.Api.Tests.Services;

public sealed class CompetitionLeaderboardServiceTests
{
    [Fact]
    public async Task GetLeaderboardAsync_WhenCompletedMigrationRunExists_ReturnsImportedOfficialOrderWithPreseasonTotals()
    {
        var options = CreateOptions();
        var runId = Guid.NewGuid();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.Competitions.Add(new F1.Core.Models.Competition
            {
                Id = 42,
                Name = "Philip 2025",
                Year = 2025,
                Description = "Philip canonical competition"
            });

            dbContext.Races.Add(new F1.Core.Models.Race
            {
                Id = "aus-2025",
                CompetitionId = 42,
                Season = 2025,
                Round = 1,
                RaceName = "Australian Grand Prix",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            });

            dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
            {
                Id = 100,
                CompetitionId = 42,
                Season = 2025,
                QuestionId = "PRE-001",
                Category = F1.Core.Models.QuestionCategory.Preseason,
                Prompt = "Preseason winner",
                Status = F1.Core.Models.QuestionTemplateStatus.Published,
                SortOrder = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            dbContext.RacePickScores.AddRange(
                new RacePickScoreEntity { RaceId = "aus-2025", RaceCode = "AUS", PickType = "TOTAL", ParticipantId = "Charlie", ImportedPoints = 20, CalculatedPoints = 18, OverrideScore = 20, OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE", SourceRunId = runId, DeltaPoints = -2, ReasonCode = "RACE_TOTAL", RecordedAtUtc = DateTime.UtcNow },
                new RacePickScoreEntity { RaceId = "aus-2025", RaceCode = "AUS", PickType = "TOTAL", ParticipantId = "Alice", ImportedPoints = 20, CalculatedPoints = 17, OverrideScore = 20, OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE", SourceRunId = runId, DeltaPoints = -3, ReasonCode = "RACE_TOTAL", RecordedAtUtc = DateTime.UtcNow },
                new RacePickScoreEntity { RaceId = "aus-2025", RaceCode = "AUS", PickType = "TOTAL", ParticipantId = "Bob", ImportedPoints = 10, CalculatedPoints = 25, OverrideScore = 10, OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE", SourceRunId = runId, DeltaPoints = 15, ReasonCode = "RACE_TOTAL", RecordedAtUtc = DateTime.UtcNow },
                new RacePickScoreEntity { RaceId = "aus-2025", RaceCode = "AUS", PickType = "CDP", ParticipantId = "Alice", ImportedPoints = 50, CalculatedPoints = 50, OverrideScore = 50, OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE", SourceRunId = runId, DeltaPoints = 0, ReasonCode = "CDP_TIE_BREAK", RecordedAtUtc = DateTime.UtcNow });

            dbContext.QuestionScores.AddRange(
                new QuestionScoreEntity { QuestionTemplateId = 100, ParticipantId = "Alice", ImportedPoints = 5, CalculatedPoints = 4, OverrideScore = 5, OverrideReasonCode = "PRESEASON_EXACT", OverrideSourceRunId = runId, DeltaPoints = -1, RecordedAtUtc = DateTime.UtcNow },
                new QuestionScoreEntity { QuestionTemplateId = 100, ParticipantId = "Bob", ImportedPoints = 5, CalculatedPoints = 2, OverrideScore = 5, OverrideReasonCode = "PRESEASON_EXACT", OverrideSourceRunId = runId, DeltaPoints = -3, RecordedAtUtc = DateTime.UtcNow });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = CreateService(serviceContext);

        var result = await service.GetLeaderboardAsync("philip", 2025, "active", isAdmin: false, CancellationToken.None);

        Assert.True(result.IsDataAvailable);
        Assert.Equal("Official Source: Imported legacy scores", result.ScoreSourceLabel);
        Assert.Equal(["Alice", "Charlie", "Bob"], result.Items.Select(item => item.ParticipantName).ToArray());
        Assert.Equal([25, 20, 15], result.Items.Select(item => item.DisplayPoints).ToArray());
        Assert.Equal([1, 2, 3], result.Items.Select(item => item.Position).ToArray());
    }

    [Fact]
    public async Task GetLeaderboardAsync_WhenAdminRequestsRecalculated_ReturnsRecalculatedOrdering()
    {
        var options = CreateOptions();
        var runId = Guid.NewGuid();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.Competitions.Add(new F1.Core.Models.Competition
            {
                Id = 42,
                Name = "Philip 2025",
                Year = 2025,
                Description = "Philip canonical competition"
            });

            dbContext.Races.Add(new F1.Core.Models.Race
            {
                Id = "aus-2025",
                CompetitionId = 42,
                Season = 2025,
                Round = 1,
                RaceName = "Australian Grand Prix",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            });

            dbContext.RacePickScores.AddRange(
                new RacePickScoreEntity { RaceId = "aus-2025", RaceCode = "AUS", PickType = "TOTAL", ParticipantId = "Alice", ImportedPoints = 25, CalculatedPoints = 12, OverrideScore = 25, OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE", SourceRunId = runId, DeltaPoints = -13, ReasonCode = "RACE_TOTAL", RecordedAtUtc = DateTime.UtcNow },
                new RacePickScoreEntity { RaceId = "aus-2025", RaceCode = "AUS", PickType = "TOTAL", ParticipantId = "Bob", ImportedPoints = 15, CalculatedPoints = 30, OverrideScore = 15, OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE", SourceRunId = runId, DeltaPoints = 15, ReasonCode = "RACE_TOTAL", RecordedAtUtc = DateTime.UtcNow });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = CreateService(serviceContext);

        var result = await service.GetLeaderboardAsync("philip", 2025, "recalculated", isAdmin: true, CancellationToken.None);

        Assert.Equal("Compare Mode: Recalculated scores", result.ScoreSourceLabel);
        Assert.Equal(["Bob", "Alice"], result.Items.Select(item => item.ParticipantName).ToArray());
        Assert.Equal([30, 12], result.Items.Select(item => item.DisplayPoints).ToArray());
    }

    [Fact]
    public async Task GetLeaderboardAsync_WhenContextUnavailable_ReturnsEmptyState()
    {
        var options = CreateOptions();

        await using var dbContext = new F1DbContext(options);
        var service = CreateService(dbContext);

        var result = await service.GetLeaderboardAsync("main", 2026, "active", isAdmin: false, CancellationToken.None);

        Assert.False(result.IsDataAvailable);
        Assert.Equal("Leaderboard data is not available for this competition yet.", result.EmptyStateMessage);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetLeaderboardAsync_WhenH2hScoresExist_IncludesThemInLeaderboardTotals()
    {
        var options = CreateOptions();
        var runId = Guid.NewGuid();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.Competitions.Add(new F1.Core.Models.Competition
            {
                Id = 42,
                Name = "Philip 2025",
                Year = 2025,
                Description = "Philip canonical competition"
            });

            dbContext.Races.Add(new F1.Core.Models.Race
            {
                Id = "aus-2025",
                CompetitionId = 42,
                Season = 2025,
                Round = 1,
                RaceName = "Australian Grand Prix",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            });

            dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
            {
                Id = 500,
                CompetitionId = 42,
                Season = 2025,
                QuestionId = "H2H-001",
                Category = F1.Core.Models.QuestionCategory.H2H,
                Prompt = "Who finishes ahead?",
                Status = F1.Core.Models.QuestionTemplateStatus.Published,
                SortOrder = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            dbContext.RacePickScores.Add(new RacePickScoreEntity
            {
                RaceId = "aus-2025",
                RaceCode = "AUS",
                PickType = "TOTAL",
                ParticipantId = "Alice",
                ImportedPoints = 20,
                CalculatedPoints = 20,
                OverrideScore = null,
                OverrideReasonCode = null,
                SourceRunId = runId,
                DeltaPoints = 0,
                ReasonCode = "RACE_TOTAL",
                RecordedAtUtc = DateTime.UtcNow
            });

            dbContext.QuestionScores.Add(new QuestionScoreEntity
            {
                QuestionTemplateId = 500,
                ParticipantId = "Alice",
                ImportedPoints = 5,
                CalculatedPoints = 5,
                OverrideScore = null,
                OverrideReasonCode = null,
                OverrideSourceRunId = runId,
                DeltaPoints = 0,
                RecordedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = CreateService(serviceContext);

        var result = await service.GetLeaderboardAsync("philip", 2025, "active", isAdmin: false, CancellationToken.None);

        Assert.True(result.IsDataAvailable);
        Assert.Single(result.Items);
        Assert.Equal("Alice", result.Items[0].ParticipantName);
        Assert.Equal(25, result.Items[0].DisplayPoints);
    }

    [Fact]
    public async Task GetLeaderboardAsync_WhenDavidContextConfiguredAndCompetitionStoredAsDave_ReturnsData()
    {
        var options = CreateOptions();
        var runId = Guid.NewGuid();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.Competitions.Add(new F1.Core.Models.Competition
            {
                Id = 77,
                Name = "Dave 2025",
                Year = 2025,
                Description = "Dave canonical competition"
            });

            dbContext.Races.Add(new F1.Core.Models.Race
            {
                Id = "dave-aus-2025",
                CompetitionId = 77,
                Season = 2025,
                Round = 1,
                RaceName = "Australian Grand Prix",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            });

            dbContext.RacePickScores.Add(new RacePickScoreEntity
            {
                RaceId = "dave-aus-2025",
                RaceCode = "AUS",
                PickType = "TOTAL",
                ParticipantId = "DavidJ",
                ImportedPoints = 42,
                CalculatedPoints = 40,
                OverrideScore = 42,
                OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE",
                SourceRunId = runId,
                DeltaPoints = -2,
                ReasonCode = "RACE_TOTAL",
                RecordedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = CreateService(serviceContext);

        var result = await service.GetLeaderboardAsync("david", 2025, "active", isAdmin: false, CancellationToken.None);

        Assert.True(result.IsDataAvailable);
        Assert.False(result.IsComparisonAvailable);
        Assert.Equal("recalculated", result.ScoreView);
        Assert.Single(result.Items);
        Assert.Equal("DavidJ", result.Items[0].ParticipantName);
        Assert.Equal(40, result.Items[0].DisplayPoints);
    }

    [Fact]
    public async Task GetParticipantDetailAsync_ReturnsRacePreseasonAndH2hSections()
    {
        var options = CreateOptions();
        var runId = Guid.NewGuid();

        await using (var dbContext = new F1DbContext(options))
        {
            dbContext.Competitions.Add(new F1.Core.Models.Competition
            {
                Id = 42,
                Name = "Philip 2025",
                Year = 2025,
                Description = "Philip 2025 season competition"
            });

            dbContext.Races.Add(new F1.Core.Models.Race
            {
                Id = "aus-2025",
                CompetitionId = 42,
                Season = 2025,
                Round = 1,
                RaceName = "Australian Grand Prix",
                CircuitName = "albert_park",
                StartTimeUtc = DateTime.UtcNow,
                PreQualyDeadlineUtc = DateTime.UtcNow,
                FinalDeadlineUtc = DateTime.UtcNow
            });

            dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
            {
                Id = 100,
                CompetitionId = 42,
                Season = 2025,
                QuestionId = "h2h-aus-1",
                Category = F1.Core.Models.QuestionCategory.H2H,
                Prompt = "Who finishes higher?",
                Status = F1.Core.Models.QuestionTemplateStatus.Published,
                SortOrder = 1,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            dbContext.QuestionTemplates.Add(new QuestionTemplateEntity
            {
                Id = 101,
                CompetitionId = 42,
                Season = 2025,
                QuestionId = "WDC",
                Category = F1.Core.Models.QuestionCategory.Preseason,
                Prompt = "Who wins the championship?",
                Status = F1.Core.Models.QuestionTemplateStatus.Published,
                SortOrder = 2,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });

            dbContext.QuestionScores.Add(new QuestionScoreEntity
            {
                QuestionTemplateId = 100,
                ParticipantId = "Alice",
                ImportedPoints = 1,
                CalculatedPoints = 2,
                DeltaPoints = 1,
                RecordedAtUtc = DateTime.UtcNow
            });

            dbContext.QuestionScores.Add(new QuestionScoreEntity
            {
                QuestionTemplateId = 101,
                ParticipantId = "Alice",
                ImportedPoints = 4,
                CalculatedPoints = 6,
                OverrideScore = 4,
                OverrideReasonCode = "PRESEASON_CORRECT",
                OverrideSourceRunId = runId,
                DeltaPoints = 2,
                RecordedAtUtc = DateTime.UtcNow
            });

            dbContext.RacePickScores.Add(new RacePickScoreEntity
            {
                RaceId = "aus-2025",
                RaceCode = "AUS",
                PickType = "1",
                ParticipantId = "Alice",
                ImportedPoints = 3,
                CalculatedPoints = 5,
                OverrideScore = 3,
                OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE",
                SourceRunId = runId,
                DeltaPoints = 2,
                ReasonCode = "RACE_CORRECT",
                Explanation = "Exact pick",
                RecordedAtUtc = DateTime.UtcNow
            });

            dbContext.RacePickScores.Add(new RacePickScoreEntity
            {
                RaceId = "aus-2025",
                RaceCode = "AUS",
                PickType = "CDP",
                ParticipantId = "Alice",
                ImportedPoints = 99,
                CalculatedPoints = 99,
                OverrideScore = 99,
                OverrideReasonCode = "MIGRATION_IMPORTED_OVERRIDE",
                SourceRunId = runId,
                DeltaPoints = 0,
                ReasonCode = "CDP_TIE_BREAK",
                Explanation = "Tie break only",
                RecordedAtUtc = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync();
        }

        await using var serviceContext = new F1DbContext(options);
        var service = CreateService(serviceContext);

        var result = await service.GetParticipantDetailAsync("philip", 2025, "Alice", CancellationToken.None);

        Assert.Single(result.RacePicks.Items);
        Assert.Single(result.Preseason.Items);
        Assert.Single(result.H2h.Items);
        Assert.Equal(3, result.RacePicks.ImportedTotalPoints);
        Assert.Equal(6, result.Preseason.RecalculatedTotalPoints);
        Assert.Equal("Who finishes higher?", result.H2h.Items[0].Description);
    }

    private static CompetitionLeaderboardService CreateService(F1DbContext dbContext)
    {
        var options = Options.Create(new CompetitionLeaderboardOptions
        {
            Contexts =
            [
                new CompetitionLeaderboardContextOption
                {
                    CompetitionSlug = "philip",
                    Season = 2025,
                    DisplayName = "Philip 2025",
                    SourceType = "MigrationRun",
                    ActiveScoreSource = "ImportedLegacy",
                    MigrationSourcePathContains = "phil-2025"
                },
                new CompetitionLeaderboardContextOption
                {
                    CompetitionSlug = "david",
                    Season = 2025,
                    DisplayName = "David 2025",
                    SourceType = "MigrationRun",
                    ActiveScoreSource = "ImportedLegacy",
                    MigrationSourcePathContains = "dave-2025"
                },
                new CompetitionLeaderboardContextOption
                {
                    CompetitionSlug = "main",
                    Season = 2026,
                    DisplayName = "Main 2026",
                    SourceType = "Unavailable",
                    UnavailableMessage = "Leaderboard data is not available for this competition yet."
                }
            ]
        });

        return new CompetitionLeaderboardService(dbContext, options);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(builder => builder.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }
}