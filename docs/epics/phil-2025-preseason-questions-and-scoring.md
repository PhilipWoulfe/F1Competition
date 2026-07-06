# Epic #275: Phil 2025 Preseason Questions and Scoring

## Summary
Add end-to-end support for preseason questions in the Phil 2025 migration flow, including parsing, scoring, reconciliation, API exposure, admin UI review, and automated test coverage.

This epic isolates preseason logic from race scoring so preseason data is first-class and auditable without contaminating race totals.

## Problem Statement
Preseason data exists in the source CSV but is currently out of scope for race scoring. Without a dedicated implementation path, preseason values can be misinterpreted as race points, producing incorrect totals (for example, accidental 20-point race entries).

## Goals
- Parse and store preseason question answers and actual answers.
- Parse and persist preseason points-per-question policy from M2.
- Parse and persist preseason tally rows separately from race points.
- Recalculate preseason points with explicit rules and compare imported vs calculated values.
- Expose preseason run data through admin APIs and admin UI views.
- Provide clear automated test coverage across worker, API, and web surfaces.

## Scope
In scope:
- CSV adapter support for preseason row ranges and mappings.
- Preseason domain entities, storage, and reconciliation output.
- Admin API endpoints for preseason run summaries and detail.
- Admin web views for preseason expected-vs-actual diffs.
- Unit, integration, and E2E coverage for preseason flows.

Out of scope:
- Changes to podium or DNF race scoring rules.
- Multi-season preseason redesign outside Phil 2025 source format.

## CSV Contract for Preseason Slice
- Participant columns: B=Philip, C=New Sexy Ayrton, D=Andy, E=Claire, F=Dave, G=Kevin, H=Pious, I=Shane, J=Veronica, K=BinGPT.
- Actual answer column: L=Actual Answers.
- Preseason points policy cell: M2=points to award per preseason question.
- Row range 2-21: preseason questions and participant answers.
- Row range 22-41: preseason point tallies.

## Data and Domain Expectations
- Preseason points must be stored separately from race points.
- Preseason totals must be reported separately from race totals.
- Combined season totals must be explicit composition: race total + preseason total.
- Imported preseason tallies and recalculated preseason tallies must both be retained for comparison.

## User Stories
### Story P1: Define preseason import contract and section classifier
As an engineer, I want a formal preseason contract so rows 2-41 are parsed deterministically.

Acceptance criteria:
- Classifier identifies preseason question rows (2-21) and preseason tally rows (22-41).
- Contract documents participant columns B-K, actual column L, and policy cell M2.
- Row-level validation errors include source row number and reason.

Test notes:
- Add classifier tests for valid rows, blank separators, and malformed preseason rows.
- Verify deterministic row classification across repeated runs.

### Story P2: Parse preseason question answers and actual outcomes
As a maintainer, I want preseason answer rows normalized so scoring input is reliable.

Acceptance criteria:
- Parser extracts question identifier/text, participant answers, and actual answers from L.
- Empty, NONE, NOT, and whitespace-only values normalize to null.
- Parsed rows persist source row number for traceability.

Test notes:
- Add unit tests for normalization and malformed answer tokens.
- Add fixture tests for multi-token or delimited actual answers where applicable.

### Story P3: Parse and persist preseason points policy and imported tallies
As an analyst, I want policy and imported tallies persisted so imported-vs-calculated preseason totals can be reconciled.

Acceptance criteria:
- M2 is parsed as preseason points-per-question policy metadata for the run.
- Rows 22-41 imported tallies are stored per participant with row references.
- Missing or malformed M2/tally values follow policy-driven behavior (hard-fail or warning).

Test notes:
- Add tests for valid, missing, and malformed M2 values.
- Add tests for tally row parse behavior and policy handling.

### Story P4: Implement preseason scoring calculator
As a product owner, I want preseason points recalculated from explicit rules so preseason scoring is reproducible.

Acceptance criteria:
- Calculator awards points per answered question based on M2 policy and actual answer matching rules.
- Rule behavior for null answers and unmatched answers is explicit and documented.
- Calculated preseason totals are persisted separately from imported tallies.

Test notes:
- Add matrix tests for exact match, null, mismatched, and malformed answer paths.
- Add edge-case tests for missing policy or missing actual answer rows.

### Story P5: Produce preseason reconciliation outputs
As a reviewer, I want preseason explainability output so scoring differences are actionable.

Acceptance criteria:
- Reconciliation output exists at participant-question and participant-total levels.
- Each diff row includes imported points, calculated points, delta, reason code, and explanation.
- Preseason summary includes per-participant net delta and top reason categories.

Test notes:
- Add deterministic ordering tests for preseason diff output.
- Verify explanation text exists for every non-zero delta.

### Story P6: Add preseason data to migration run read APIs
As an admin, I want preseason migration data in API responses so I can review preseason correctness without CLI access.

Acceptance criteria:
- Admin run detail API includes preseason summary, participant preseason totals, and question-level diffs.
- API contracts provide deterministic sorting/filtering for preseason sections.
- Authorization behavior remains admin-only.

Test notes:
- Add API integration tests for preseason payload shapes and auth behavior.
- Verify non-admin callers receive forbidden responses.

### Story P7: Build admin preseason review UI
As an admin reviewer, I want dedicated preseason views so I can validate preseason results quickly.

Acceptance criteria:
- Migration run detail UI includes a preseason tab or section with summary cards.
- UI shows question-level expected-vs-actual comparisons with filters by participant and non-zero delta.
- UI supports export for preseason reconciliation output aligned with runbook needs.

Test notes:
- Add UI integration tests for loading, empty, error, and filtered states.
- Verify preseason rows and totals are visually distinct from race scoring rows.

### Story P8: End-to-end preseason execution path and safeguards
As an operator, I want reliable preseason execution controls so runs are repeatable and safe.

Acceptance criteria:
- Dry-run and write-run both execute preseason pipeline and emit preseason summary counters.
- Policy guard prevents preseason points from being merged into race-only totals by mistake.
- Run metadata captures preseason parse/scoring status and warning/error counts.

Test notes:
- Add command-level integration tests for dry-run/write modes.
- Add regression test for accidental race contamination scenario (for example, unexpected 20-point race values from preseason rows).

### Story P9: Complete preseason test strategy and CI gates
As a team, I want explicit preseason test coverage gates so regressions are caught early.

Acceptance criteria:
- Unit tests cover preseason classifier, parser, normalization, and scoring logic.
- Integration tests cover worker persistence and admin API preseason payloads.
- E2E test validates admin preseason review flow from run list to question-level detail.
- Coverage report includes preseason logic paths and meets team quality gates.

Test notes:
- Add dedicated test fixtures for preseason rows and malformed inputs.
- Add CI test filter docs for quick preseason validation runs.

### Story P10: Document preseason runbook and sign-off checklist
As a team, we want preseason runbook steps so execution and approval are consistent.

Implementation reference:
- Runbook and checklist: `docs/runbooks/preseason-migration-signoff.md`

Acceptance criteria:
- Runbook documents prerequisites, command usage, and validation steps for preseason data.
- Sign-off checklist includes M2 policy verification, question-level diff review, and preseason-vs-race separation check.
- Documentation links API and UI preseason review locations.

Test notes:
- Dry-run the documented flow in a clean environment.
- Verify checklist can be followed without requiring tribal knowledge.

## Delivery Plan
1. P1 contract and classifier
2. P2 parser for question answers and actuals
3. P3 policy and imported tally persistence
4. P4 preseason scoring calculator
5. P5 preseason reconciliation outputs
6. P6 API contract and endpoint updates
7. P7 admin UI preseason views
8. P8 end-to-end safeguards and run metadata
9. P9 full test strategy and CI gate coverage
10. P10 runbook and sign-off

## Risks and Mitigations
- Risk: Preseason values leak into race totals.
- Mitigation: hard separation of preseason and race entities plus guardrail tests.

- Risk: Ambiguous preseason question parsing due to source row variability.
- Mitigation: strict classifier plus row-level validation diagnostics.

- Risk: UI reviewers miss preseason discrepancies.
- Mitigation: dedicated preseason comparison view with non-zero delta filters.

## Definition of Done
- Preseason rows 2-41 are parsed, persisted, and reconciled deterministically.
- Preseason and race scoring are fully separated in storage, API, and UI.
- Admin can review preseason expected-vs-actual diffs end-to-end without CLI.
- Unit, integration, and E2E preseason tests pass in CI with required coverage targets.
- Runbook and sign-off checklist are complete and validated.
