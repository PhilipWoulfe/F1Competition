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
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
                SourceFileChecksum = "abc123",
                Status = "Completed",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                FinishedAtUtc = DateTime.UtcNow
            });

            dbContext.MigrationImportParticipantDeltaSummaries.AddRange(
                new MigrationImportParticipantDeltaSummaryEntity { ImportRunId = runId, Subject = "Charlie", ImportedTotalPoints = 20, CalculatedTotalPoints = 18 },
                new MigrationImportParticipantDeltaSummaryEntity { ImportRunId = runId, Subject = "Alice", ImportedTotalPoints = 20, CalculatedTotalPoints = 17 },
                new MigrationImportParticipantDeltaSummaryEntity { ImportRunId = runId, Subject = "Bob", ImportedTotalPoints = 10, CalculatedTotalPoints = 25 });

            dbContext.MigrationImportPreseasonParticipantDeltaSummaries.AddRange(
                new MigrationImportPreseasonParticipantDeltaSummaryEntity { ImportRunId = runId, Subject = "Alice", ImportedTotalPoints = 5, CalculatedTotalPoints = 4 },
                new MigrationImportPreseasonParticipantDeltaSummaryEntity { ImportRunId = runId, Subject = "Bob", ImportedTotalPoints = 5, CalculatedTotalPoints = 2 });

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
            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
                SourceFileChecksum = "abc123",
                Status = "Completed",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                FinishedAtUtc = DateTime.UtcNow
            });

            dbContext.MigrationImportParticipantDeltaSummaries.AddRange(
                new MigrationImportParticipantDeltaSummaryEntity { ImportRunId = runId, Subject = "Alice", ImportedTotalPoints = 25, CalculatedTotalPoints = 12 },
                new MigrationImportParticipantDeltaSummaryEntity { ImportRunId = runId, Subject = "Bob", ImportedTotalPoints = 15, CalculatedTotalPoints = 30 });

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

            dbContext.QuestionScores.Add(new QuestionScoreEntity
            {
                QuestionTemplateId = 100,
                ParticipantId = "Alice",
                ImportedPoints = 1,
                CalculatedPoints = 2,
                DeltaPoints = 1,
                RecordedAtUtc = DateTime.UtcNow
            });

            dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
            {
                Id = runId,
                SourceFilePath = "data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv",
                SourceFileChecksum = "abc123",
                Status = "Completed",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                FinishedAtUtc = DateTime.UtcNow
            });

            dbContext.MigrationImportPickDiffs.Add(new MigrationImportPickDiffEntity
            {
                ImportRunId = runId,
                RaceCode = "AUS",
                PickType = "1",
                Subject = "Alice",
                ImportedPoints = 3,
                CalculatedPoints = 5,
                DeltaPoints = 2,
                ReasonCode = "RACE_CORRECT",
                Explanation = "Exact pick"
            });

            dbContext.MigrationImportPreseasonQuestionDiffs.Add(new MigrationImportPreseasonQuestionDiffEntity
            {
                ImportRunId = runId,
                RowNumber = 1,
                QuestionKey = "WDC",
                QuestionText = "Who wins the championship?",
                Subject = "Alice",
                ImportedPoints = 4,
                CalculatedPoints = 6,
                DeltaPoints = 2,
                ReasonCode = "PRESEASON_CORRECT",
                Explanation = "Matched answer"
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