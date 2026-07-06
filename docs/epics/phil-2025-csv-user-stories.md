# Phil 2025 Migration Story Backlog

## Context
- Source file for this slice: data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv
- Parent epic: docs/epics/phil-2025-csv-migration-and-scoring-reconciliation.md
- Canonical race identity: season + round
- This source format is one-time, but the import core must support additional adapters.

## Story M1: Create import staging model and run envelope
As a developer, I want a staging model and import run envelope so every row is traceable and reruns are auditable.

Acceptance criteria:
- A staging schema/table set exists for raw CSV rows with row number, section type, and raw column payload.
- Import run record persists source file path, source checksum, started/finished timestamps, and run status.
- Re-running the same file in dry-run mode does not write domain entities.

Test notes:
- Verify the same checksum is produced across repeated reads.
- Verify dry-run records diagnostics and summary counters only.

## Story M2: Implement row classifier for mixed CSV sections
As a maintainer, I want deterministic row classification so prediction rows, legacy score rows, and totals are parsed correctly.

Acceptance criteria:
- Classifier identifies: season question predictions, season question points, race picks, race points, totals/meta rows.
- Bahrain special label BAH-HUMBUG is normalized to DNF pick type during classification.
- Classifier outputs row-level reason when classification fails.

Test notes:
- Add fixtures with blank separators, repeated headers, and malformed labels.
- Assert row-to-section mapping is stable.

## Story M3: Parse race picks and L outcomes from source rows
As a developer, I want race picks and L outcomes parsed into normalized records so scoring input is complete.

Acceptance criteria:
- For each race block, parser extracts P1, P2, P3, and optional DNF picks per participant.
- L row is parsed as authoritative actual outcomes for the corresponding race block.
- Empty, NONE, NOT, and whitespace-only values normalize to null.

Test notes:
- Cover rows with no DNF actuals and rows with multi-token DNF actuals.
- Cover malformed L rows with missing columns.

## Story M4: Build canonical mapping from Jolpica 2025 schedule
As a maintainer, I want race mapping based on season rounds so ambiguous race labels do not break joins.

Acceptance criteria:
- Jolpica 2025 race schedule snapshot is persisted for the run.
- Race blocks are aligned to canonical rounds by sequence.
- MON ambiguity (Monaco and Monza) resolves through round position, not row label text.
- Conflicting label-to-round mappings generate warnings.

Test notes:
- Unit test MON occurrences map to distinct canonical rounds.
- Verify warning output when row label text mismatches canonical race naming.

## Story M5: Implement token normalization dictionary and unresolved queue
As an operator, I want known aliases normalized and unknown tokens surfaced so we avoid silent scoring drift.

Acceptance criteria:
- Known aliases map as specified: MAX->VER, HULK->HUL, Bear Man->BEAR.
- NONE/NOT/blank normalize to null selection.
- Unknown tokens are persisted in unresolved-token output with row references.
- Unresolved-token policy supports fail-on-threshold configuration.

Test notes:
- Include mixed-case and whitespace-variant alias fixtures.
- Assert unresolved tokens never auto-map without explicit dictionary entry.

## Story M6: Implement recalculation engine for podium and DNF
As a product owner, I want scoring recalculated from explicit rules so canonical points are reproducible.

Acceptance criteria:
- Podium scoring: exact=10, top3-wrong-slot=5, otherwise=0.
- DNF scoring: optional pick, correct pick=5, blank+no-actual-dnf=5, blank+actual-dnf=0, wrong pick=0.
- Multiple actual DNFs award full DNF points on first match.
- No negative scoring.

Test notes:
- Add matrix tests for all podium and DNF permutations.
- Include cases with multiple actual DNFs and blank picks.

## Story M7: Import legacy points and totals alongside calculated results
As an analyst, I want legacy points preserved so old-vs-new comparison remains transparent.

Acceptance criteria:
- Legacy race-level points are imported and linked to participant/race/pick where available.
- Legacy final totals are stored as imported snapshots.
- Calculated totals are stored separately and never overwrite imported totals.

Test notes:
- Validate imported totals and calculated totals can diverge without data loss.
- Verify null-safe handling where legacy values are missing.

## Story M8: Produce reconciliation diffs with explainability
As a reviewer, I want reasoned variance output so differences are actionable.

Acceptance criteria:
- Diff output exists at participant-race level and participant-race-pick level.
- Each diff row includes imported points, calculated points, delta, reason code, and explanation text.
- Summary report includes per-participant net delta and top reason categories.

Test notes:
- Golden-file test for deterministic diff output ordering.
- Verify explanation text exists for every non-zero delta.

## Story M9: Add import CLI/service entrypoint for one-time execution
As an engineer, I want a repeatable command path so this migration can be run and audited reliably.

Acceptance criteria:
- Import entrypoint accepts source file path, season, and dry-run flag.
- Default path supports data/imports/phil-2025/PhilMigratedSelectionsAndScores.csv.
- Command prints run summary: rows parsed, rows rejected, unresolved tokens, total deltas.

Test notes:
- Smoke test command for dry-run and write modes.
- Verify non-zero exit for malformed file or unresolved-token hard-fail.

## Story M10: Document runbook, admin validation, and sign-off checklist
As a team, we want a migration runbook and admin validation flow so execution and approval are consistent.

Acceptance criteria:
- Runbook documents prerequisites, command usage, and rollback/cleanup steps.
- Sign-off checklist includes: unresolved token review, MON mapping review, totals variance review, and admin UI validation of run detail data.
- Final report artifact locations are documented.

Test notes:
- Dry-run the runbook in a clean environment and verify no missing steps.
- Validate the runbook includes explicit admin UI verification steps for runs list, run detail, and expected-vs-actual comparisons.

## Story M11: Add admin read API for migration runs and run detail
As an admin, I want migration run APIs so the UI can show historical runs and detailed reconciliation output.

Acceptance criteria:
- Admin-only endpoints return paged migration runs with status, timestamps, source file, dry-run flag, and summary counters.
- Run detail endpoint returns unresolved token summary, participant deltas, race diffs, and pick diffs for a selected run.
- Endpoint contracts include deterministic ordering and stable filtering by run status/date.

Test notes:
- API contract tests for list/detail payloads and authorization behavior.
- Verify non-admin callers receive forbidden responses.

## Story M12: Build admin migrations runs page
As an admin, I want to browse migration runs in the admin area so I can quickly inspect run health and outcomes.

Acceptance criteria:
- Admin navigation includes a Migration Runs page with paged/sortable run history.
- List view shows run id, started/finished times, status, dry-run/write mode, unresolved token count, and total delta.
- Selecting a run opens detail view with summary cards and links to comparison sections.

Test notes:
- UI integration tests for list rendering, empty state, loading state, and error state.
- Verify only admin users can access the page route.

## Story M13: Add expected-vs-actual comparison UX for run detail
As an admin reviewer, I want expected-vs-actual comparisons so I can understand exactly where migration values differ.

Acceptance criteria:
- Run detail includes participant-race and participant-race-pick comparisons showing imported points (expected), calculated points (actual), delta, reason code, and explanation.
- Comparison tables support filtering by participant, race, non-zero delta only, and reason code.
- Detail panel or expandable row shows raw values used to compute the comparison where available.

Test notes:
- UI tests for filter behavior and deterministic table ordering.
- Verify explanation text is always present for non-zero deltas.

## Story M14: Add admin reconciliation export and audit trail affordances
As an admin, I want export and audit affordances so run reviews can be shared and traced.

Acceptance criteria:
- Run detail supports downloading reconciliation output (CSV or JSON) for participant and pick diffs.
- Admin actions and view access are auditable with run id and timestamp.
- Export output format aligns with runbook sign-off requirements.

Test notes:
- Verify export payload schema and row ordering are stable.
- Verify audit records are written for admin access and export actions.

## Suggested Delivery Sequence
1. M1 staging and run envelope
2. M2 row classifier
3. M3 parser for picks and L outcomes
4. M4 canonical round mapping via Jolpica
5. M5 normalization and unresolved queue
6. M6 scoring engine
7. M7 legacy import and total snapshots
8. M8 reconciliation output
9. M9 runnable entrypoint
10. M11 admin read API
11. M12 admin runs page
12. M13 expected-vs-actual comparison UX
13. M14 export and audit affordances
14. M10 runbook and final sign-off
