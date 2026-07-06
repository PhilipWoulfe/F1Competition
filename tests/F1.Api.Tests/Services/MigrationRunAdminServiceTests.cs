using System.Text.Json;
using F1.Api.Dtos;
using F1.Api.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace F1.Api.Tests.Services;

public sealed class MigrationRunAdminServiceTests
{
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

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(new[] { "zzz_race", "aaa_race" }, detail!.PickDiffs.Select(x => x.RaceCode).ToArray());
        Assert.Equal(new[] { "zzz_race", "aaa_race" }, detail.RaceDiffs.Select(x => x.RaceCode).ToArray());

        var export = await service.ExportRunDiffsAsync(runId, "pick-diffs", "json", "admin@example.com", CancellationToken.None);
        Assert.NotNull(export);
        Assert.True(export!.Success);

        var exportedRows = JsonSerializer.Deserialize<AdminMigrationPickDiffDto[]>(
            export.Payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(exportedRows);
        Assert.Equal(new[] { "zzz_race", "aaa_race" }, exportedRows!.Select(x => x.RaceCode).ToArray());
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

        var detail = await service.GetRunDetailAsync(runId, "admin@example.com", CancellationToken.None);

        Assert.NotNull(detail);
        Assert.True(detail!.PickDiffs.Single().IsExpectedVariance);
        Assert.Equal("KNOWN_LEGACY_POINTS_ERROR", detail.PickDiffs.Single().ExpectedVarianceReasonCode);
        Assert.Equal("phil-aus-1-expected", detail.PickDiffs.Single().ExpectedVarianceRuleId);
        Assert.True(detail.RaceDiffs.Single().IsExpectedVariance);
    }

    private static DbContextOptions<F1DbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"migration-run-admin-service-{Guid.NewGuid():N}")
            .Options;
    }
}