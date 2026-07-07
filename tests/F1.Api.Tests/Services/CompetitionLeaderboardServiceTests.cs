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