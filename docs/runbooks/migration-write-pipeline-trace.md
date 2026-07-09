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
  - `RacePickScores` (materialized from imported legacy pick scores plus recalculated pick scores)

## Intended Canonical Targets (Epic Contract)
For write mode, canonical race-domain entities are created or updated where applicable:
- `Drivers` (created when not already present)
- `Races` (pre-seeded required; looked up by circuit id or round — not created by migration writer)
- `Selections` (created or reused per conflict policy)
- `SelectionPositions` (replaced per selection)
- `RacePickScores` (runtime race-pick score storage, including imported, calculated, and effective override channels)

Question-domain tables already receive canonical writes via parser/scoring:
- `QuestionAnswers`
- `QuestionActuals`
- `QuestionScores`

## Migration-To-Canonical Mapping Matrix
The current handoff boundary is:

| Migration / staging source | Canonical target | Notes |
| --- | --- | --- |
| `MigrationImportRaceSelections` | `Selections` + `SelectionPositions` | Uses mapped canonical `RaceId`; conflict policy controls overwrite vs skip/fail. |
| `MigrationImportCalculatedScores` + `MigrationImportLegacyPickScores` | `RacePickScores` | Imported legacy score becomes `OverrideScore` when it differs from calculated points. |
| `QuestionAnswers` | `QuestionAnswers` | Canonical question answers are written during parsing, not by the later canonical writer. |
| `QuestionActuals` | `QuestionActuals` | Canonical actuals are written during parsing, not by the later canonical writer. |
| `MigrationImportPreseasonImportedTallies` + generic question scoring output | `QuestionScores` | Imported points are preserved; effective runtime uses `OverrideScore ?? CalculatedPoints`. |
| `MigrationImportRaceRoundMappings` | `Races` lookup only | Staging mapping is not runtime storage; it resolves canonical `RaceId` during handoff. |
| Reconciliation tables such as `MigrationImportPickDiffs`, `MigrationImportParticipantDeltaSummaries`, `MigrationImportPreseasonQuestionDiffs` | No canonical target | Admin/audit only; runtime must not depend on them. |

## Current Notes (Observed)
1. Mapping/enrichment executes for both dry-run and write runs.
2. Canonical race-domain persistence is write-mode only and is now implemented.
3. Rollback paths and conflict diagnostics are available for canonical-write operations.
4. Product leaderboard and participant detail reads are expected to keep working even after reconciliation staging rows are deleted, because runtime now reads `RacePickScores` and `QuestionScores` only.

## Follow-On Implementation Work
- Continue hardening rollback scope and non-empty DB safeguards.
- Expand operational runbooks for conflict policies and post-write verification.
- Keep migration and schema drift checks enforced in CI for rollout safety.
