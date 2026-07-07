# Migration Write Pipeline Trace and Gap Report

## Scope
This trace documents the current migration import execution path from kickoff to persistence and identifies why write mode does not currently materialize race-domain entities in canonical tables.

## End-to-End Pipeline Map
1. Run kickoff
- Entry points: worker orchestrator `MigrationImportOrchestrator.RunOnceAsync` and queued mode `RunNextQueuedAsync`.
- Run metadata row is created/claimed in `MigrationImportRunService`.

2. Raw row staging
- CSV rows are classified and persisted to `MigrationImportRawRows`.

3. Parsing and normalization
- Race picks are parsed into `MigrationImportRaceSelections`.
- Generic/preseason question inputs are parsed into:
  - `MigrationImportPreseasonAnswers`
  - `QuestionAnswers`
  - `QuestionActuals`

4. Mapping and enrichment (write mode only)
- Race sequence mapping persists into:
  - `MigrationImportJolpicaRaceSnapshots`
  - `MigrationImportRaceRoundMappings`
- Race codes in staged selections are rewritten to mapped circuit ids.

5. Scoring
- Race pick scoring persists to `MigrationImportCalculatedScores`.
- Imported legacy totals persist to `MigrationImportLegacyPickScores` and related totals tables.
- Generic question scoring persists to `QuestionScores`.

6. Reconciliation
- Diff and summary outputs persist to migration reconciliation tables:
  - `MigrationImportPickDiffs`
  - `MigrationImportRaceDiffs`
  - `MigrationImportParticipantDeltaSummaries`
  - `MigrationImportReasonCategorySummaries`
  - preseason diff/summary companion tables

7. Completion
- Run status and metadata are written to `MigrationImportRuns`.

## Intended Canonical Targets (Epic Contract)
For write mode, canonical race-domain entities are expected to be materialized where applicable:
- `Drivers`
- `Races`
- `Selections`
- `SelectionPositions`

Question-domain tables already receive run-scoped writes via parser/scoring (`QuestionAnswers`, `QuestionActuals`, `QuestionScores`).

## Current Gaps (Observed)
1. Missing canonical race-domain persistence step
- No service currently upserts `Drivers`, `Races`, `Selections`, or `SelectionPositions` from migration import outputs.

2. No transaction boundary spanning canonical writes
- Existing per-stage persistence is transactional only at EF SaveChanges granularity; there is no single transaction ensuring all-or-nothing canonical materialization.

3. Write-mode side effect mismatch
- `DryRun=false` triggers mapping and reconciliation side effects but still does not materialize race-domain canonical entities.

## Characterization Evidence
- Test: `RunOnceAsync_WhenWriteModeEnabled_DoesNotPersistCanonicalRaceDomainEntitiesYet` in `tests/F1.Infrastructure.Tests/Relational/MigrationImportRunServiceTests.cs`.
- Behavior captured:
  - migration import staging/reconciliation tables populated
  - canonical race-domain tables remain empty

## Follow-On Implementation Work
- Add a canonical write service that executes after reconciliation and before run completion.
- Wrap canonical writes in a transaction and fail run on partial-write errors.
- Add idempotent upsert keys for reruns and non-empty DB policy enforcement.
