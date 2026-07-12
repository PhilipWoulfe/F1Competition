using F1.DataSyncWorker.Models;
using F1.DataSyncWorker.Services;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.Infrastructure.Tests.Contracts;

public sealed class MigrationImportRunServiceMultiFileStagingTests
{
    [Fact]
    public async Task StageRowsAsync_WhenRowsShareNumberAcrossFiles_PersistsBothRowsWithSourceFileProvenance()
    {
        var options = new DbContextOptionsBuilder<F1DbContext>()
            .UseInMemoryDatabase($"multi-file-stage-{Guid.NewGuid():N}")
            .Options;

        var runId = Guid.NewGuid();
        await using var seed = new F1DbContext(options);
        seed.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = "/tmp/dave-2025",
            SourceFileChecksum = "abc",
            IsDryRun = true,
            Status = "Started",
            StartedAtUtc = DateTime.UtcNow
        });
        await seed.SaveChangesAsync();

        var service = new MigrationImportRunService(new TestDbContextFactory(options));
        await service.StageRowsAsync(
            runId,
            [
                new StagedImportRow(1, MigrationImportSectionTypes.Header, "Name,Race1-1", SourceFileName: "races.csv"),
                new StagedImportRow(1, MigrationImportSectionTypes.Header, "Question,Answer", SourceFileName: "bonusAnswers.csv")
            ],
            CancellationToken.None);

        await using var verify = new F1DbContext(options);
        var rows = await verify.MigrationImportRawRows
            .Where(x => x.ImportRunId == runId)
            .OrderBy(x => x.SourceFileName)
            .ThenBy(x => x.RowNumber)
            .ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal("bonusAnswers.csv", rows[0].SourceFileName);
        Assert.Equal("races.csv", rows[1].SourceFileName);
        Assert.Equal(1, rows[0].RowNumber);
        Assert.Equal(1, rows[1].RowNumber);
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

        public Task<F1DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new F1DbContext(_options));
        }
    }
}
