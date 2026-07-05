using System.Security.Cryptography;
using System.Text;
using F1.DataSyncWorker.Models;
using F1.Infrastructure.Data;
using F1.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace F1.DataSyncWorker.Services;

public sealed class MigrationImportRunService : IMigrationImportRunService
{
    private const string StatusStarted = "Started";
    private const string StatusCompleted = "Completed";
    private const string StatusFailed = "Failed";
    private readonly IDbContextFactory<F1DbContext> _dbContextFactory;

    public MigrationImportRunService(IDbContextFactory<F1DbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<MigrationImportRunContext> StartRunAsync(string sourceFilePath, bool isDryRun, CancellationToken cancellationToken)
    {
        var checksum = await ComputeSha256Async(sourceFilePath, cancellationToken);
        var runId = Guid.NewGuid();

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        dbContext.MigrationImportRuns.Add(new MigrationImportRunEntity
        {
            Id = runId,
            SourceFilePath = sourceFilePath,
            SourceFileChecksum = checksum,
            IsDryRun = isDryRun,
            Status = StatusStarted,
            StartedAtUtc = DateTime.UtcNow
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MigrationImportRunContext(
            runId,
            sourceFilePath,
            checksum,
            isDryRun,
            PersistDomainEntities: !isDryRun);
    }

    public async Task StageRowsAsync(Guid runId, IReadOnlyCollection<StagedImportRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var createdAtUtc = DateTime.UtcNow;

        var entities = rows.Select(row => new MigrationImportRawRowEntity
        {
            ImportRunId = runId,
            RowNumber = row.RowNumber,
            SectionType = row.SectionType,
            RawPayload = row.RawPayload,
            CreatedAtUtc = createdAtUtc
        });

        dbContext.MigrationImportRawRows.AddRange(entities);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteRunAsync(Guid runId, int rawRowCount, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, StatusCompleted, rawRowCount, errorMessage: null, cancellationToken);
    }

    public async Task FailRunAsync(Guid runId, string errorMessage, CancellationToken cancellationToken)
    {
        await UpdateRunAsync(runId, StatusFailed, rawRowCount: null, errorMessage, cancellationToken);
    }

    private async Task UpdateRunAsync(Guid runId, string status, int? rawRowCount, string? errorMessage, CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await dbContext.MigrationImportRuns.FirstOrDefaultAsync(x => x.Id == runId, cancellationToken)
            ?? throw new InvalidOperationException($"Migration import run {runId} was not found.");

        run.Status = status;
        run.FinishedAtUtc = DateTime.UtcNow;
        run.ErrorMessage = Truncate(errorMessage, 4000);

        if (rawRowCount.HasValue)
        {
            run.RawRowCount = rawRowCount.Value;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string sourceFilePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Migration source file was not found.", sourceFilePath);
        }

        await using var stream = File.OpenRead(sourceFilePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        var builder = new StringBuilder(hash.Length * 2);

        foreach (var value in hash)
        {
            builder.Append(value.ToString("x2"));
        }

        return builder.ToString();
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}