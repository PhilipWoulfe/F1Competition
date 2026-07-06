# Preseason Migration Runbook and Sign-Off Checklist

## Purpose

Provide a repeatable operator workflow for Phil 2025 preseason migration verification.

This runbook covers prerequisites, command usage, API/UI validation, and sign-off criteria so preseason review does not rely on tribal knowledge.

## Scope

- In scope:
  - Preseason import/scoring/reconciliation validation for migration runs.
  - Admin API and UI preseason review checks.
  - Sign-off checklist before approving a run.
- Out of scope:
  - Changing race scoring logic.
  - Modifying source CSV structure.
  - Expected-variance rule governance (see expected variance runbook).

## Prerequisites

1. Access and environment:
- Admin access to the API/UI environment under test.
- Connectivity to Postgres used by the environment.
- Migration source CSV available at the configured path.

2. Required runtime settings (worker):
- `MigrationImport__Enabled=true`
- `MigrationImport__Season=2025`
- `MigrationImport__SourceFilePath=data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv`
- `MigrationImport__DryRun=true` for validation runs

3. Optional safeguards:
- `MigrationImport__UnresolvedTokenFailThreshold` for fail-fast unresolved-token behavior.

## Execution Steps

### 1. Run a Dry-Run Preseason Import

```bash
ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=f1competition;Username=f1;Password=f1' \
  dotnet run --project src/F1.DataSyncWorker -- \
  --migration-import --dry-run --season 2025 \
  --source-file-path data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv
```

Capture the `RunId` from logs or query latest completed run:

```bash
psql "$ConnectionStrings__Postgres" -c "
SELECT \"Id\", \"Status\", \"StartedAtUtc\", \"FinishedAtUtc\"
FROM \"MigrationImportRuns\"
ORDER BY \"StartedAtUtc\" DESC
LIMIT 5;"
```

### 2. Verify Preseason Policy (M2)

Validate that policy row was parsed and persisted:

```bash
psql "$ConnectionStrings__Postgres" -c "
SELECT \"ImportRunId\", \"CellReference\", \"RawPointsPerQuestion\", \"PointsPerQuestion\"
FROM \"MigrationImportPreseasonPolicies\"
WHERE \"ImportRunId\" = '<RUN_ID>';
"
```

Expected:
- `CellReference = 'M2'`
- `PointsPerQuestion` is non-null for valid inputs.
- If null, run metadata should reflect preseason warnings.

### 3. Verify Run Metadata and Safeguards

```bash
psql "$ConnectionStrings__Postgres" -c "
SELECT \"Id\", \"Status\", \"UnresolvedTokenCount\", \"MappingWarningCount\",
       \"PreseasonParseStatus\", \"PreseasonScoringStatus\",
       \"PreseasonWarningCount\", \"PreseasonErrorCount\",
       \"PreseasonAnswerCount\", \"PreseasonScoredQuestionCount\",
       \"PreseasonQuestionDiffCount\", \"PreseasonTotalDeltaPoints\",
       \"PreseasonIsolationGuardPassed\"
FROM \"MigrationImportRuns\"
WHERE \"Id\" = '<RUN_ID>';
"
```

Expected:
- `Status = Completed` for successful runs.
- `PreseasonIsolationGuardPassed = true`.
- Non-zero preseason counters when preseason rows are present.
- Warning/error counts align with source data quality.

### 4. Verify Preseason-vs-Race Separation Check

Preseason rows must not leak into race legacy points.

```bash
psql "$ConnectionStrings__Postgres" -c "
WITH preseason_rows AS (
  SELECT \"RowNumber\"
  FROM \"MigrationImportRawRows\"
  WHERE \"ImportRunId\" = '<RUN_ID>'
    AND \"SectionType\" IN ('SeasonQuestionPrediction', 'SeasonQuestionPoints')
)
SELECT COUNT(*) AS contamination_count
FROM \"MigrationImportLegacyPickScores\" l
JOIN preseason_rows p ON p.\"RowNumber\" = l.\"RowNumber\"
WHERE l.\"ImportRunId\" = '<RUN_ID>';
"
```

Expected:
- `contamination_count = 0`

### 5. Validate API Review Locations

Use admin API detail and export endpoints:

- `GET /admin/migration-runs/{runId}`
- `GET /admin/migration-runs/{runId}?expectedStatus=all`
- `GET /admin/migration-runs/{runId}/exports/preseason-question-diffs?format=csv`
- `GET /admin/migration-runs/{runId}/exports/preseason-question-diffs?format=json`
- `GET /admin/migration-runs/{runId}/exports/preseason-participant-diffs?format=csv`
- `GET /admin/migration-runs/{runId}/exports/preseason-participant-diffs?format=json`

Expected:
- `PreseasonSummary`, `PreseasonParticipantDeltas`, `PreseasonQuestionDiffs`, and `PreseasonReasonCategorySummaries` populated and deterministic.

### 6. Validate UI Review Locations

Open admin migration runs page:

- `/admin/migration-runs`
- Preseason anchor: `/admin/migration-runs#preseason-comparisons`

Verify:
- Preseason summary cards render.
- Preseason participant totals table renders.
- Preseason question diffs table renders.
- Preseason filters (`participant`, `reason`, `non-zero`) behave correctly.
- Preseason export links are available and downloadable.

## Write-Run Validation (Optional)

After dry-run sign-off, execute write mode:

```bash
ConnectionStrings__Postgres='Host=localhost;Port=5432;Database=f1competition;Username=f1;Password=f1' \
  dotnet run --project src/F1.DataSyncWorker -- \
  --migration-import --write-mode --season 2025 \
  --source-file-path data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv
```

Repeat metadata/API/UI validation steps for the new run.

## Sign-Off Checklist

Mark each item before approving the run:

- [ ] Dry-run completed successfully with recorded `RunId`.
- [ ] M2 policy row verified (`CellReference='M2'`) and `PointsPerQuestion` validated.
- [ ] Preseason parse/scoring statuses reviewed from run metadata.
- [ ] Preseason warning/error counts reviewed and accepted.
- [ ] Question-level preseason diffs reviewed in API detail response.
- [ ] Question-level preseason diffs reviewed in UI preseason section.
- [ ] Preseason participant totals reviewed in API/UI.
- [ ] Preseason export artifacts downloaded (CSV or JSON) and attached to review record.
- [ ] Preseason-vs-race separation check passed (`contamination_count=0`).
- [ ] `PreseasonIsolationGuardPassed=true` confirmed.
- [ ] Approval record captured (reviewer, timestamp, run id, notes).

## Troubleshooting

- Missing preseason sections in API/UI:
  - Confirm run has preseason rows and `PreseasonAnswerCount > 0`.
  - Confirm latest migrations are applied.

- Preseason warnings present:
  - Check `MigrationImportPreseasonPolicies` and `MigrationImportPreseasonImportedTallies` for null parse outcomes.

- Isolation guard failure:
  - Inspect legacy pick scores joined against preseason row numbers.
  - Do not approve run until contamination root cause is fixed.

## Related References

- Epic story source: `docs/epics/phil-2025-preseason-questions-and-scoring.md`
- Preseason testing strategy: `docs/testing/preseason-test-strategy.md`
- Expected variance governance: `docs/runbooks/expected-variance-governance.md`
