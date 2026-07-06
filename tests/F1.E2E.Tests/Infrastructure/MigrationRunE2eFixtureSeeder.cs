using Npgsql;

namespace F1.E2E.Tests.Infrastructure;

internal sealed record MigrationRunE2eFixture(
    Guid RunId,
    string PrimaryParticipant,
    string SecondaryParticipant,
    string RaceCode,
    string ReasonCode,
    string PickType);

internal static class MigrationRunE2eFixtureSeeder
{
    private static readonly Guid FixtureRunId = Guid.Parse("9f814ff9-81ff-4876-b2e4-0eb2bb11db8d");

    public static async Task<MigrationRunE2eFixture> EnsureSeededAsync(
        E2eOptions options,
        Action<string>? trace,
        CancellationToken cancellationToken)
    {
        var connectionString = options.PostgresConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Deterministic migration-run E2E requires a Postgres connection string. " +
                "Set E2E_POSTGRES_CONNECTION_STRING or POSTGRES_* variables.");
        }

        const string sourcePath = "fixtures/e2e/migration-run.csv";
        const string sourceChecksum = "e2e-migration-fixture-v1";
        const string status = "Completed";

        const string primaryParticipant = "fixture-admin";
        const string secondaryParticipant = "fixture-other";
        const string raceCode = "tst-r01";
        const string reasonCode = "FixtureReasonDelta";
        const string pickType = "1";

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var startedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        var finishedAtUtc = DateTime.UtcNow;

        await using (var upsertRun = new NpgsqlCommand(
                         """
                         INSERT INTO "MigrationImportRuns" ("Id", "SourceFilePath", "SourceFileChecksum", "IsDryRun", "Status", "StartedAtUtc", "FinishedAtUtc", "RawRowCount", "ErrorMessage")
                         VALUES (@runId, @sourcePath, @sourceChecksum, false, @status, @startedAtUtc, @finishedAtUtc, 2, NULL)
                         ON CONFLICT ("Id") DO UPDATE
                         SET "SourceFilePath" = EXCLUDED."SourceFilePath",
                             "SourceFileChecksum" = EXCLUDED."SourceFileChecksum",
                             "IsDryRun" = EXCLUDED."IsDryRun",
                             "Status" = EXCLUDED."Status",
                             "StartedAtUtc" = EXCLUDED."StartedAtUtc",
                             "FinishedAtUtc" = EXCLUDED."FinishedAtUtc",
                             "RawRowCount" = EXCLUDED."RawRowCount",
                             "ErrorMessage" = NULL;
                         """,
                         connection,
                         transaction))
        {
            upsertRun.Parameters.AddWithValue("runId", FixtureRunId);
            upsertRun.Parameters.AddWithValue("sourcePath", sourcePath);
            upsertRun.Parameters.AddWithValue("sourceChecksum", sourceChecksum);
            upsertRun.Parameters.AddWithValue("status", status);
            upsertRun.Parameters.AddWithValue("startedAtUtc", startedAtUtc);
            upsertRun.Parameters.AddWithValue("finishedAtUtc", finishedAtUtc);
            await upsertRun.ExecuteNonQueryAsync(cancellationToken);
        }

        await ClearRunChildrenAsync(connection, transaction, cancellationToken);

        await using (var insertParticipant = new NpgsqlCommand(
                         """
                         INSERT INTO "MigrationImportParticipantDeltaSummaries"
                             ("ImportRunId", "Subject", "ImportedTotalPoints", "CalculatedTotalPoints", "NetDeltaPoints", "TopReasonCode", "TopReasonCount")
                         VALUES
                             (@runId, @primaryParticipant, 100, 106, 6, @reasonCode, 1),
                             (@runId, @secondaryParticipant, 40, 40, 0, 'FixtureReasonMatch', 1);
                         """,
                         connection,
                         transaction))
        {
            insertParticipant.Parameters.AddWithValue("runId", FixtureRunId);
            insertParticipant.Parameters.AddWithValue("primaryParticipant", primaryParticipant);
            insertParticipant.Parameters.AddWithValue("secondaryParticipant", secondaryParticipant);
            insertParticipant.Parameters.AddWithValue("reasonCode", reasonCode);
            await insertParticipant.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertRaceDiffs = new NpgsqlCommand(
                         """
                         INSERT INTO "MigrationImportRaceDiffs"
                             ("ImportRunId", "RaceCode", "Subject", "ImportedPoints", "CalculatedPoints", "DeltaPoints", "ReasonCode", "Explanation")
                         VALUES
                             (@runId, @raceCode, @primaryParticipant, 18, 21, 3, @reasonCode, 'fixture non-zero race delta'),
                             (@runId, @raceCode, @secondaryParticipant, 9, 9, 0, 'FixtureReasonMatch', 'fixture zero race delta');
                         """,
                         connection,
                         transaction))
        {
            insertRaceDiffs.Parameters.AddWithValue("runId", FixtureRunId);
            insertRaceDiffs.Parameters.AddWithValue("raceCode", raceCode);
            insertRaceDiffs.Parameters.AddWithValue("primaryParticipant", primaryParticipant);
            insertRaceDiffs.Parameters.AddWithValue("secondaryParticipant", secondaryParticipant);
            insertRaceDiffs.Parameters.AddWithValue("reasonCode", reasonCode);
            await insertRaceDiffs.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertPickDiffs = new NpgsqlCommand(
                         """
                         INSERT INTO "MigrationImportPickDiffs"
                             ("ImportRunId", "RaceCode", "PickType", "Subject", "ImportedPoints", "CalculatedPoints", "DeltaPoints", "ReasonCode", "Explanation")
                         VALUES
                             (@runId, @raceCode, @pickType, @primaryParticipant, 6, 8, 2, @reasonCode, 'fixture non-zero pick delta'),
                             (@runId, @raceCode, '2', @secondaryParticipant, 4, 4, 0, 'FixtureReasonMatch', 'fixture zero pick delta');
                         """,
                         connection,
                         transaction))
        {
            insertPickDiffs.Parameters.AddWithValue("runId", FixtureRunId);
            insertPickDiffs.Parameters.AddWithValue("raceCode", raceCode);
            insertPickDiffs.Parameters.AddWithValue("pickType", pickType);
            insertPickDiffs.Parameters.AddWithValue("primaryParticipant", primaryParticipant);
            insertPickDiffs.Parameters.AddWithValue("secondaryParticipant", secondaryParticipant);
            insertPickDiffs.Parameters.AddWithValue("reasonCode", reasonCode);
            await insertPickDiffs.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insertReasonSummary = new NpgsqlCommand(
                         """
                         INSERT INTO "MigrationImportReasonCategorySummaries"
                             ("ImportRunId", "ReasonCode", "OccurrenceCount", "TotalDeltaPoints")
                         VALUES
                             (@runId, @reasonCode, 2, 5),
                             (@runId, 'FixtureReasonMatch', 2, 0);
                         """,
                         connection,
                         transaction))
        {
            insertReasonSummary.Parameters.AddWithValue("runId", FixtureRunId);
            insertReasonSummary.Parameters.AddWithValue("reasonCode", reasonCode);
            await insertReasonSummary.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        trace?.Invoke($"Migration fixture seeded. RunId={FixtureRunId}");

        return new MigrationRunE2eFixture(
            FixtureRunId,
            primaryParticipant,
            secondaryParticipant,
            raceCode,
            reasonCode,
            pickType);
    }

    private static async Task ClearRunChildrenAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var deleteSql =
            """
            DELETE FROM "MigrationImportReasonCategorySummaries" WHERE "ImportRunId" = @runId;
            DELETE FROM "MigrationImportPickDiffs" WHERE "ImportRunId" = @runId;
            DELETE FROM "MigrationImportRaceDiffs" WHERE "ImportRunId" = @runId;
            DELETE FROM "MigrationImportParticipantDeltaSummaries" WHERE "ImportRunId" = @runId;
            DELETE FROM "MigrationImportUnresolvedTokens" WHERE "ImportRunId" = @runId;
            DELETE FROM "MigrationImportRaceSelections" WHERE "ImportRunId" = @runId;
            DELETE FROM "MigrationImportRawRows" WHERE "ImportRunId" = @runId;
            """;

        await using var command = new NpgsqlCommand(deleteSql, connection, transaction);
        command.Parameters.AddWithValue("runId", FixtureRunId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
