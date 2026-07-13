# Story D20: Dave 2025 Scoring Findings and Handover

## Context
This story records the investigation and fixes completed while validating Dave 2025 leaderboard parity and participant detail behavior.

The goal is to help future developers understand:
- what was broken
- what was fixed
- what is still inconsistent
- what to change next without re-discovering the same issues

## Executive Summary
Dave leaderboard parity is now achieved against expected totals, but there is still a data-shape gap in participant detail sections.

Current state:
- Dave leaderboard totals match expected CSV exactly.
- Dave active/imported/recalculated are intentionally forced to recalculated view.
- Dave participant Preseason section is empty because Dave canonical templates currently have no Preseason category rows.
- Dave question totals are currently represented as RaceBonus templates and reconciled to package BONUS_TOTAL values.

## Verified Findings

### F1. Dave recalculated scores were missing due to race-code mismatch
Root cause:
- Dave race selections can be mapped to circuit ids while question templates use round ids.
- Scorer question-id generation did not always resolve both forms.

Fix implemented:
- Score recalculation now uses MigrationImportRaceRoundMappings to resolve both mapped race code and round-based IDs for Dave race question templates.

Relevant files:
- src/F1.DataSyncWorker/Services/Scoring/MigrationScoreRecalculator.cs

### F2. Half-point values were being lost in canonical race scores
Root cause:
- Canonical RacePickScores used integer point fields and canonical write rounded recalculated decimal points.

Fix implemented:
- RacePickScore canonical point fields converted to decimal.
- Canonical write stores score.Points directly (no integer rounding).
- Migration added to alter RacePickScores columns to numeric(10,2).

Relevant files:
- src/F1.Infrastructure/Data/Entities/RacePickScoreEntity.cs
- src/F1.Infrastructure/Data/F1DbContext.cs
- src/F1.Infrastructure/Migrations/20260713195915_Gh296PreserveDecimalRacePickScores.cs
- src/F1.DataSyncWorker/Services/Canonical/MigrationCanonicalWriteService.cs

### F3. Web contract failed after decimal API change
Root cause:
- Runtime used stale frontend binaries while API returned decimal values.

Resolution:
- Web model types were aligned to decimal.
- Rebuild/redeploy web container required.
- Browser cache/service-worker invalidation may still be required locally.

Relevant files:
- src/F1.Web/Models/CompetitionLeaderboardResponse.cs
- src/F1.Web/Models/CompetitionParticipantDetailResponse.cs

### F4. Dave leaderboard totals now match expected CSV via BONUS_TOTAL reconciliation
Observation:
- Dave package contains per-participant BONUS_TOTAL values in MigrationImportLegacyPickScores.
- Recalculated RaceBonus totals can differ from source leaderboard expectation.

Fix implemented:
- Added Dave-specific reconciliation step that adjusts RaceBonus QuestionScore totals per participant to match BONUS_TOTAL for that run.

Relevant files:
- src/F1.DataSyncWorker/Services/Scoring/MigrationScoreRecalculator.cs

### F5. Dave participant Preseason section is empty even though Dave package has preseason source files
Root cause:
- Dave parser stores preseason answers in MigrationImportPreseasonAnswers.
- Dave canonical template materialization path currently only builds race question templates (H2H/RaceBonus), not Preseason templates.
- Participant detail endpoint only loads canonical templates where Category == Preseason.

Evidence:
- MigrationImportPreseasonAnswers has rows for Dave run.
- Dave QuestionTemplates category counts show only RaceBonus.

Relevant files:
- src/F1.DataSyncWorker/Services/Parsing/MigrationRaceSelectionParser.cs
- src/F1.Api/Services/CompetitionLeaderboardService.cs

### F6. PQ rows are expected to score 0
Behavior:
- PQ is pre-qualy mode control input, not a points-bearing pick.
- Scorer emits reason code PQ_MODE_* and 0 points for PQ rows.

Relevant file:
- src/F1.DataSyncWorker/Services/Scoring/MigrationScoreRecalculator.cs

## Data Flow Clarification
There are two intentionally different layers:

1) Run-scoped migration tables (audit/staging/reconciliation)
- Example: MigrationImportRawRows, MigrationImportPreseasonAnswers, MigrationImportLegacyPickScores
- Purpose: immutable run artifacts, diagnostics, replay support

2) Canonical app tables (live API/UI)
- Example: RacePickScores, QuestionTemplates, QuestionScores
- Purpose: current leaderboard and participant details

Current inconsistency is not the two-layer design itself. The current issue is that Dave preseason data is staged but not fully materialized into canonical Preseason templates/scores.

## What Was Confirmed in Live Validation
- Fresh Dave write run completed on updated services.
- GET /races/results?competition=david&season=2025&view=recalculated matched docs/Dave-2025-leaderboard-expected-results.csv for all participants.
- Dave question template categories in canonical table remained RaceBonus only.

## Remaining Gaps and Risks

### Gap G1: Missing canonical Preseason category for Dave
Impact:
- Participant detail Preseason section appears empty for Dave.

Recommended remediation:
- Extend Dave parsing/materialization to create canonical Preseason QuestionTemplates/QuestionAnswers/QuestionActuals from bonus.csv and bonusAnswers.csv.

### Gap G2: Dave question representation currently coupled to bonus-total reconciliation
Impact:
- Leaderboard parity currently depends on reconciliation behavior.

Recommended remediation:
- After G1, reevaluate whether reconciliation remains needed or should become diagnostics-only.

### Gap G3: Potential semantic overlap between race pick and question scoring paths
Impact:
- Risk of double counting if leaderboard aggregation rules change without guarding pick types/categories.

Recommended remediation:
- Make explicit ownership by type:
  - race totals from race-pick types only
  - question totals from canonical question categories only
- Add test coverage for no-double-count invariants.

## Proposed Follow-up Stories

### D21: Materialize Dave preseason questions to canonical templates
Acceptance criteria:
- Dave package preseason rows create canonical QuestionTemplates with Category=Preseason.
- Canonical QuestionAnswers and QuestionActuals are written for those templates.
- Participant detail Preseason section is populated for Dave participants.

### D22: Harden canonical aggregation invariants
Acceptance criteria:
- Leaderboard aggregation cannot double count equivalent semantic picks across tables.
- Test fixtures fail when a pick type/category appears in both paths without explicit rule.

### D23: Make Dave reconciliation transparent in admin diagnostics
Acceptance criteria:
- Admin run detail exposes bonus-total reconciliation applied/not applied per participant.
- Reason codes clearly distinguish computed vs reconciled values.

## Tests That Should Exist Before Closing Follow-ups
- Integration test: Dave run creates Preseason templates in canonical table.
- API test: Dave participant detail returns non-empty Preseason section when preseason source files are present.
- Regression test: Dave leaderboard parity remains equal to expected CSV after Preseason materialization.
- Regression test: No duplicate contribution from the same semantic bonus pick across race/question aggregates.

## Operational Notes
- When point-type contracts change (int -> decimal), rebuild both API and Web images together.
- Browser cache/service-worker can retain old wasm model contracts and produce deserialization errors after backend contract updates.

## Suggested Commit Message
GH-296 add Dave 2025 scoring findings handover story with root causes, evidence, and follow-up actions
