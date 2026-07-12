# Epic: Dave 2025 Multi-File Migration and Scoring Reconciliation

## Summary
Migrate the Dave 2025 competition into canonical competition data using the existing migration run pipeline, while adding a new source adapter for a multi-file input bundle.

This epic delivers deterministic import, score recalculation, reconciliation against legacy leaderboard outputs, and admin review controls for the second competition source.

## Why This Epic
Current migration flow is contract-coupled to the Phil 2025 single-file CSV shape. The Dave 2025 competition uses a different source model and scoring rules:
- Separate files for race picks, preseason questions, preseason answers, and leaderboard snapshots.
- Different points rules (including pre-qualifying multipliers, all-picks jackpot, and race bonus variants).
- Different participant roster and question text corpus.

Without a dedicated adapter and explicit run contract, importing Dave 2025 risks silent score drift and ambiguous mapping behavior.

## Source Bundle (Dave 2025)
Expected source package (same run scope/checksum boundary):
- races.csv
- bonus.csv
- bonusAnswers.csv
- Leaderboard.csv
- raceResults.ps1 (rule-reference artifact)
- MostOF the boionus Questions.txt (question metadata supplement)

## Problem Statement
The second competition source is not shape-compatible with the current pipeline contract:
- Current parser/classifier expects one staged CSV stream and Phil-specific row windows.
- Legacy score importer expects race points embedded in the same staged dataset.
- Current question scoring strategies do not yet cover all Dave-specific bonus/track rule variants.

## Goals
- Add a first-class source adapter for Dave 2025 multi-file ingestion.
- Preserve imported legacy totals and component-level scores for reconciliation.
- Recalculate race picks, preseason, H2H, and race bonus from explicit rules.
- Produce explainable diffs at pick/race/participant/component levels.
- Keep migration deterministic, idempotent, and auditable in dry-run and write modes.

## Non-Goals
- Rewriting Phil 2025 adapter logic beyond shared abstractions needed for multi-source support.
- Redesigning generic gameplay UX outside admin migration workflows.
- Implementing historical multi-season ingestion beyond Dave 2025.

## Existing Platform Constraints (Current State)
- Migration run envelope, reconciliation artifacts, and admin run UI already exist.
- Canonical write service and conflict policy handling already exist.
- Question framework supports categories: Preseason, H2H, RaceBonus.
- Phil contract policy is file-name coupled and must be generalized to source profiles.
- Legacy importer and parser currently assume one staged CSV and row-window semantics.

## Dave 2025 Domain Rules (To Encode)
Baseline race scoring from rule script:
- Podium picks:
  - Normal mode: exact = 10, top3 wrong slot = 5.
  - Pre-qualy mode Yes: exact = 15, top3 wrong slot = 7.5.
- Pre-qualy mode All:
  - If P1/P2/P3 and DNF all correct, award 100.
  - Otherwise no All-mode jackpot points.
- DNF:
  - Correct DNF match = 5 in normal/pre-qualy Yes modes.
  - Supports multi-token actual DNF lists and None/NoDNF semantics.
- H2H:
  - Correct pick = 5 when race defines H2H and answer provided.
- Race bonus question:
  - Default exact match = 20.
  - Track-specific variants exist (for example SAU gap formula, MON/GBR +/-1 tolerance pattern).
- Preseason bonus:
  - Correct answer = +30 per question.
- Tie-break:
  - CDP (correct podium picks count) as secondary sort key after total.

## Data Contract (Target State)
- Canonical competition identity supports distinct Dave 2025 competition rows, independent from Phil 2025.
- Migration run records preserve source bundle fingerprint and file manifest.
- Imported totals include component splits where available:
  - Race points
  - Preseason bonus points
  - Combined total/final values
- Calculated totals preserve same component splits and recomposed total.
- Reconciliation outputs include component-level and aggregate deltas.

## Key Risks and Mitigations
- Risk: Rule drift between script behavior and implemented scoring.
  - Mitigation: golden fixture tests based on provided source files and scripted expected outcomes.
- Risk: Ambiguous bonus question semantics from free-text sources.
  - Mitigation: question metadata model with explicit scoring mode per question and manual override support.
- Risk: Multi-file package inconsistency (participant mismatches, missing race columns).
  - Mitigation: strict preflight validation and fail-fast contract diagnostics.
- Risk: Non-empty DB collisions across competitions.
  - Mitigation: enforce competition-season scoping and conflict diagnostics before commit.

## User Stories

### Story D1: Define Dave 2025 source package contract
As an engineer, I want a formal source-bundle contract so multi-file ingestion is deterministic and auditable.

Acceptance criteria:
- Contract documents required files, optional files, and schema expectations for each file.
- Run startup validates package completeness and reports missing/extra file diagnostics.
- Run checksum includes deterministic file-manifest hashing (path + content hash per file).

Test notes:
- Add contract tests for complete package, missing file, and malformed header cases.

### Story D2: Introduce source-profile architecture for migration adapters
As a maintainer, I want adapter profiles so Phil and Dave imports can coexist without branching chaos.

Acceptance criteria:
- Migration source profile abstraction selects parser/importer behavior by source package profile.
- Existing Phil flow remains supported with no behavior regression.
- Adapter-specific logic is isolated from shared orchestration and reconciliation core.

Test notes:
- Add profile selection tests and backward-compatibility tests for Phil runs.

### Story D3: Add multi-file staging and normalized raw-row envelope
As a developer, I want multi-file staging so all Dave files become traceable import inputs.

Acceptance criteria:
- Raw staging persists file name, row number, section type, and raw payload for each staged row.
- Staging supports independent row streams per file with deterministic ordering.
- Diagnostics include file + row references in all parse/classification failures.

Test notes:
- Add staging tests verifying source provenance and ordering stability.

### Story D4: Parse races.csv into race picks and actual outcomes
As a product owner, I want race picks and actuals parsed from races.csv so core race scoring can be recalculated.

Acceptance criteria:
- Parser supports participant rows, _Result row, and RaceN-* column mapping for all rounds.
- Parsed pick types include podium picks, DNF, pre-qualy mode, H2H prompt answer, and race bonus answer.
- Null-equivalent and placeholder tokens are normalized per policy with unresolved-token reporting.

Test notes:
- Add fixture tests for missing rounds, placeholder values, and malformed RaceN column groups.

### Story D5: Parse preseason files (bonus.csv and bonusAnswers.csv)
As an operator, I want preseason answers and actuals ingested from dedicated files so preseason scoring is isolated and auditable.

Acceptance criteria:
- bonus.csv maps participant answers by question text.
- bonusAnswers.csv maps actual answer and optional notes by question text.
- Question-key reconciliation handles punctuation/whitespace normalization with collision diagnostics.

Test notes:
- Add tests for mismatched question text variants and unresolved question-key joins.

### Story D6: Import legacy leaderboard components from Leaderboard.csv
As an analyst, I want imported component totals preserved so reconciliation can compare like-for-like totals.

Acceptance criteria:
- Import race points, bonus points, total/final values, and CDP where present.
- Imported component values are persisted separately from calculated values.
- Parser handles numeric and decimal score formats safely.

Test notes:
- Add mapping tests for all summary columns and null/blank final columns.

### Story D8: Implement Dave race scoring strategy extensions
As a product owner, I want Dave race scoring semantics implemented exactly so recalculated outputs are trustworthy.

Acceptance criteria:
- Supports pre-qualy modes Yes, Post, and All as defined by Dave rules.
- Supports all-mode jackpot behavior and DNF matching semantics.
- Supports decimal points where applicable (for example 7.5 increments).

Test notes:
- Add matrix tests for all podium/pre-qualy/DNF combinations and decimal rounding behavior.

### Story D9: Implement Dave race bonus rule modes
As an admin, I want race bonus question scoring modes configurable per race so special-track logic is transparent.

Acceptance criteria:
- Supports exact-match mode and tolerance/range-based modes.
- Supports formula-based mode (for example max-minus-gap scoring pattern) where configured.
- Mode metadata is persisted with question templates/options and shown in admin review detail.

Test notes:
- Add tests for SAU-style gap formula and MON/GBR tolerance mode examples.

### Story D10: Implement Dave H2H and preseason points policy support
As a maintainer, I want policy-driven points for H2H and preseason so scoring configuration is source-accurate.

Acceptance criteria:
- H2H points-per-correct is configurable per source profile (Dave default 5).
- Preseason points-per-question is configurable per source profile (Dave default 30).
- Policy values are persisted in run metadata and visible in admin run detail.

Test notes:
- Add tests proving policy override changes only intended scoring components.

### Story D11: Add componentized reconciliation outputs for Dave totals
As an analyst, I want race and preseason component diffs so total variance can be explained quickly.

Acceptance criteria:
- Reconciliation includes component-level imported vs calculated deltas.
- Participant summary includes net delta and top reason categories across all components.
- CDP tie-break parity is included in reconciliation diagnostics.

Test notes:
- Add golden-file diff tests for participant summary and component breakdown ordering.

### Story D12: Extend admin run detail UI for multi-file source diagnostics
As an admin operator, I want file-level diagnostics and component breakdowns in run detail so troubleshooting is fast.

Acceptance criteria:
- Run detail shows source manifest, per-file parse counts, and contract diagnostics.
- UI includes component tabs or sections for race, preseason, and totals.
- Filters/export continue to work with deterministic sorting.

Test notes:
- Add UI tests for source manifest rendering, empty states, and export links.

### Story D13: Add run kickoff support for source package selection
As an admin, I want to choose Dave source package paths in kickoff so runs can be triggered without CLI edits.

Acceptance criteria:
- Kickoff API/UI supports selecting source profile and package root path.
- Validation rejects profile/path mismatches with clear errors.
- Duplicate active runs for identical profile + manifest checksum are blocked.

Test notes:
- Add API and UI tests for profile/path validation and duplicate conflict responses.

### Story D18: Promote Dave race-level H2H and bonus picks into question scoring flow
As an analyst, I want race-level H2H and race bonus picks from races.csv represented as question templates/answers so they score and reconcile through the same generic question pipeline.

Acceptance criteria:
- Dave package parsing materializes race-level H2H and race bonus picks into question templates, participant answers, and actual outcomes (in addition to existing race selection staging).
- H2H and race bonus scoring strategies execute for Dave runs using source-profile policies and mode metadata.
- Admin question diffs/exports include Dave race-level H2H and race bonus rows with deterministic ordering and reason codes.

Test notes:
- Add contract tests proving Dave races.csv H2H/BQ columns produce templates/answers/actuals.
- Add scoring/reconciliation tests proving H2H and race bonus deltas appear in question outputs and exports.

### Story D19: Support package archive upload for Dave migration kickoff
As an admin, I want to upload a Dave source package (for example zip containing csv/txt artifacts) from the UI so I do not need server-side path setup before starting a migration run.

Acceptance criteria:
- Kickoff upload API accepts package archive uploads for Dave profile (zip) in addition to existing single-CSV uploads.
- Uploaded archive is extracted into a run-scoped server directory under allowed import roots with path-traversal protection.
- Contract validation runs on extracted files and returns clear diagnostics for missing/extra/invalid files before run creation.
- Manifest checksum/deduplication uses extracted package contents (same behavior as path-based Dave package runs).
- Admin UI supports selecting package upload mode and shows extraction/validation errors inline.

Test notes:
- Add API tests for valid zip upload, invalid extension, traversal attempt, oversized upload, and missing required files.
- Add integration tests verifying extracted package kickoff parity with server-path kickoff (same checksum and duplicate conflict behavior).
- Add UI tests for archive upload flow, validation errors, and successful kickoff confirmation.

Completed above, uncompleted below
--------------------------

### Story D14: Add write-mode canonical handoff for second competition scope
As an operator, I want canonical writes scoped to Dave competition so data from multiple competitions does not collide.

Acceptance criteria:
- Canonical write resolves/creates Dave 2025 competition identity explicitly.
- Writes are competition-scoped for races, picks, question scores, and totals.
- Conflict diagnostics include competition scope fields.

Test notes:
- Add integration tests on non-empty DB with both Phil and Dave competitions present.

### Story D15: Add rollback and replay safety for Dave runs
As a platform maintainer, I want rollback/replay safety so incorrect Dave writes can be reverted without data loss.

Acceptance criteria:
- Rollback path supports Dave runs with full audit metadata.
- Replay of same manifest checksum is idempotent by policy.
- Replay with changed manifest produces deterministic new run metadata.

Test notes:
- Add rollback and rerun integration tests with checksum parity assertions.

### Story D16: Build Dave 2025 validation fixture set from provided artifacts
As a QA engineer, I want a canonical fixture suite so future changes can be regression-tested against known Dave outcomes.

Acceptance criteria:
- Fixture package includes races, bonus, bonusAnswers, and leaderboard snapshots.
- Expected-output fixtures capture participant totals, component totals, and rank ordering.
- CI gate runs Dave-focused migration tests alongside existing Phil tests.

Test notes:
- Add deterministic regression tests that fail on any score/rank drift.

### Story D17: Document runbook for Dave migration and sign-off
As a team, we want a repeatable runbook so dry-run, review, write-run, and approval are consistent.

Acceptance criteria:
- Runbook defines prerequisites, kickoff commands/API flow, validation queries, and rollback steps.
- Sign-off checklist includes component parity checks, unresolved token review, and rank ordering verification.
- Runbook links required exports and evidence artifacts.

Test notes:
- Dry-run runbook in a clean environment and verify no undocumented steps.



## Suggested Delivery Sequence
1. D1 source contract
2. D2 source-profile architecture
3. D3 multi-file staging
4. D4/D5 parser ingestion for race and preseason
5. D6 legacy component import (leaderboard)
6. D8/D9/D10 scoring strategy extensions
7. D18 race-level H2H/bonus question-channel parity
8. D11 reconciliation componentization
9. D13 kickoff profile support
10. D19 package archive upload kickoff
11. D12 admin run-detail enhancements
12. D14 write-mode competition scoping
13. D15 rollback/replay hardening
14. D16 regression fixture/CI gate
15. D17 runbook and sign-off

## Definition of Done
- Dave 2025 source package ingests and validates end-to-end in dry-run and write modes.
- Imported vs recalculated values reconcile with explainable component-level deltas.
- Ranking and tie-break behavior are deterministic and tested.
- Admin can kickoff, review, export, and sign off Dave runs without CLI dependence.
- Canonical write and rollback behavior is safe on non-empty multi-competition datasets.