# Epic: Phil 2025 CSV Migration and Scoring Reconciliation

## Summary
Migrate the Phil 2025 competition spreadsheet export into normalized application data, preserving legacy scored values while recalculating scores from canonical rules, and produce explainable differences between old and new scoring outputs.

This migration is the first adapter in a broader ingestion architecture:
- One-time import for this 2025 CSV format
- Follow-on import adapter for last-year data from a different source
- Ongoing adapter for current-year data source

## Problem Statement
The 2025 CSV contains multiple logical sections in one file, mixed labels, token aliases, and race code ambiguity.

Primary risks:
- Ambiguous race labels such as MON (Monaco and Monza)
- Mixed formatting for no-selection and no-DNF values
- Alias drift in tokens (for example MAX, HULK, Bear Man)
- Legacy point totals that must be preserved and compared against recalculated values

## Goals
- Ingest all usable prediction and score data from the 2025 CSV into normalized storage.
- Recalculate scores from explicit rules and compare against imported legacy points.
- Generate diff output with reason codes and human-readable explanations.
- Make migration deterministic, auditable, and repeatable for dry-run and re-run scenarios.

## Scope
In scope:
- 2025 CSV source adapter with row-section parsing
- Normalization dictionary and alias handling
- Canonical race mapping using 2025 Jolpica schedule
- Legacy points import and calculated points engine
- Race-level and pick-level comparison outputs
- Import run audit trail and unresolved-token reporting

Out of scope:
- UI redesign for migration views
- Full historical multi-season scoring redesign
- Non-2025 source adapter implementation (tracked as follow-on stories)

## Canonical Contract (Target State)
- Canonical race identity is season + round number.
- CSV race code is retained as source metadata only.
- For ambiguous race labels, round order from the race block aligns to Jolpica 2025 round sequence.
- If CSV race label conflicts with canonical race naming, round mapping wins and a warning is logged.

## Confirmed Scoring Rules
### Podium picks
- 10 points for exact position match.
- 5 points if the picked driver finishes in top 3 but in a different podium slot.
- 0 points otherwise.

### DNF pick
- DNF pick is optional.
- 5 points if picked DNF driver appears in actual DNF set.
- 5 points if no DNF was picked and there were no actual DNFs.
- 0 points if no DNF was picked and there were one or more actual DNFs.
- 0 points for wrong DNF pick.
- No negative scoring.
- If multiple actual DNFs exist, one matching picked driver is enough for full DNF points.

## CSV Parsing Rules
- Each race is represented by four prediction rows: <RACE>-1, <RACE>-2, <RACE>-3, <RACE>-DNF.
- Bahrain DNF label BAH-HUMBUG maps to standard DNF pick type.
- L rows are authoritative actual outcomes used for scoring comparisons.
- L row may provide P1/P2/P3 and optional DNF outcomes.
- Empty, NONE, NOT, and whitespace-only values normalize to no selection.

## Normalization Rules
- MAX maps to VER.
- HULK maps to HUL.
- Bear Man maps to BEAR.
- NONE and NOT map to null selection.
- Unknown tokens are stored for manual resolution and excluded from silent auto-fix.

## User Stories
### Story 1: Define migration contract and canonical dictionaries
As an engineer, I want a formal import contract so migration behavior is deterministic and testable.

Acceptance criteria:
- Data contract for staging, normalized entities, legacy points, calculated points, and diffs is documented.
- Jolpica-backed 2025 dictionaries for races, drivers, and constructors are snapshotted for the run.
- Alias dictionary is versioned and auditable.

### Story 2: Build 2025 CSV adapter
As a maintainer, I want a parser specialized for this one-time CSV layout so rows are classified correctly.

Acceptance criteria:
- Adapter detects section boundaries and race blocks.
- Adapter extracts participant predictions, L outcomes, and legacy score rows.
- Parser emits structured validation errors with row references.

### Story 3: Implement scoring calculator
As a product owner, I want scores recalculated from explicit rules so we can compare legacy and canonical results.

Acceptance criteria:
- Calculator applies confirmed podium and DNF rules exactly.
- Calculator supports optional DNF behavior and no-DNF outcome logic.
- Unit tests cover exact, partial, blank, and multi-DNF scenarios.

### Story 4: Implement reconciliation outputs
As an analyst, I want old-vs-new differences with explanations so discrepancies are easy to understand.

Acceptance criteria:
- Diff produced at participant-race level and participant-race-pick level.
- Each diff row includes imported points, calculated points, delta, reason code, and explanation text.
- Final totals are both imported and derived, with variance report.

### Story 5: Add import observability and controls
As an operator, I want repeatable runs and clear diagnostics so migration can be trusted.

Acceptance criteria:
- ImportRun records source hash, timestamps, row counts, and status.
- Dry-run mode produces full reports without writing final domain records.
- Unresolved tokens and ambiguous mappings are reported and block finalization by policy.

### Story 6: Prepare follow-on adapter framework
As a developer, I want source-adapter seams now so future imports do not require redesign.

Acceptance criteria:
- Shared normalization and scoring core is source-agnostic.
- 2025 CSV logic remains isolated in one adapter.
- Contracts for next source (last-year) and ongoing source are documented.

## Delivery Plan
1. Contract and dictionary snapshot design
2. CSV adapter and staging pipeline
3. Calculator and unit tests
4. Reconciliation report generation
5. Dry-run and run audit hardening
6. Follow-on adapter interface documentation

## Risks and Mitigations
- Risk: Ambiguous race labels lead to incorrect joins.
- Mitigation: round-based canonical mapping and warning reports.

- Risk: Alias misses cause silent score drift.
- Mitigation: unresolved-token hard-fail threshold and review queue.

- Risk: Legacy totals differ significantly from recalculated totals.
- Mitigation: pick-level explainability and reason-coded reconciliation.

## Definition of Done
- Import can execute end-to-end for the 2025 CSV with deterministic output.
- Legacy and recalculated scores are both persisted and compared.
- Diff report includes explainable reasons at race and pick granularity.
- MON ambiguity is resolved through round-order alignment and validated.
- Documentation is complete for follow-on source adapters.
