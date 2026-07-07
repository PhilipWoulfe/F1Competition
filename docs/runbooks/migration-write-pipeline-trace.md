# Migration Write Pipeline Trace and Gap Report

## Scope
This trace documents the current migration import execution path from kickoff to persistence, including dry-run mapping/enrichment and write-mode canonical materialization.

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

4. Mapping and enrichment (dry-run and write mode)
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

8. Canonical race-domain writes (write mode only)
- Canonical persistence runs after reconciliation in write mode via `MigrationCanonicalWriteService`.
- Races must be pre-seeded for the target season; the migration writer looks them up by circuit id or round and does not create new Race rows.
- Entities created/updated where applicable:
  - `Drivers` (created when missing)
  - `Selections` (created or reused per conflict policy)
  - `SelectionPositions` (replaced per selection)

## Intended Canonical Targets (Epic Contract)
For write mode, canonical race-domain entities are created or updated where applicable:
- `Drivers` (created when not already present)
- `Races` (pre-seeded required; looked up by circuit id or round — not created by migration writer)
- `Selections` (created or reused per conflict policy)
- `SelectionPositions` (replaced per selection)

Question-domain tables already receive run-scoped writes via parser/scoring (`QuestionAnswers`, `QuestionActuals`, `QuestionScores`).

## Current Notes (Observed)
1. Mapping/enrichment executes for both dry-run and write runs.
2. Canonical race-domain persistence is write-mode only and is now implemented.
3. Rollback paths and conflict diagnostics are available for canonical-write operations.

## Follow-On Implementation Work
- Continue hardening rollback scope and non-empty DB safeguards.
- Expand operational runbooks for conflict policies and post-write verification.
- Keep migration and schema drift checks enforced in CI for rollout safety.
