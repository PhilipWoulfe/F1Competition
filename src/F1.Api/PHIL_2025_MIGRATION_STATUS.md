# Phil 2025 Migration Status and Goal

## Purpose
This document defines the current API goal for the Phil 2025 competition migration and tracks what is already implemented versus what is still in progress.

Scope is intentionally limited to Phil 2025 while migration and score verification are underway.

## Planning Horizon
- Delivery scope now: Phil 2025 only.
- Architecture north star: one competition-centric relational model that can later support Phil 2025, Dave 2025, and Dave 2026 without rework.
- Delivery method: implement in small, verifiable chunks with explicit acceptance criteria.

## Overall Goal
Deliver stable, trustworthy leaderboard output for Phil 2025 by:
- preserving legacy imported totals where needed,
- recalculating canonical totals from migrated data,
- supporting side by side admin comparison,
- allowing explicit score overrides to match official standings when known legacy miscalculations exist.

## Target Model (North Star)
The long term data model remains competition-centric with a generic question engine.

Core entities:
- User
- Competition
- CompetitionEntrant
- Race
- Driver
- Team

Weekly picks entities:
- RaceSubmission
- RacePick

Generic question entities:
- QuestionDefinition
- QuestionInstance
- QuestionOption
- EntrantAnswer
- QuestionResolution

Scoring and audit entities:
- ScoreEvent
- LeaderboardSnapshot
- IngestionRun

This north star is retained to avoid dead-end Phil 2025 design choices.

## Current Focus
Only Phil 2025 is in active migration scope.

Out of scope for now:
- Dave 2025 and Dave 2026 question authoring workflows,
- generalized per race admin authored question models,
- multi competition rollout features beyond what is required for Phil 2025 migration confidence.

## Actionable Delivery Chunks

### Chunk 1 - Baseline And Canonical Coverage
Goal: ensure Phil 2025 can be represented consistently race by race and entrant by entrant.

Work:
- Verify canonical race identity and round mapping for all Phil 2025 races.
- Verify entrant identity normalization for migrated rows.
- Verify imported score ingestion completeness per participant and race.

Output:
- Verified migration run with no missing required race or participant references.
- Documented unresolved token list reduced to known and accepted exceptions.

Exit criteria:
- All Phil 2025 races are resolvable by canonical race id.
- Every leaderboard participant has deterministic identity mapping.

### Chunk 2 - Recalculation Parity Pass
Goal: calculate canonical totals and classify deltas against imported legacy totals.

Work:
- Recalculate race picks, preseason, and H2H where applicable.
- Persist reason codes and explanations for each delta category.
- Produce participant-level delta summary for admin review.

Output:
- Recalculated totals available for compare mode.
- Delta report grouped by reason code and participant.

Exit criteria:
- Every non-zero delta has a reason code and explanation.
- Admin can inspect imported versus recalculated in leaderboard views.

### Chunk 3 - Official Score Override Layer
Goal: keep displayed standings aligned with official Phil 2025 legacy standings when known miscalculations exist.

Work:
- Record explicit override scores for validated legacy miscalculation cases.
- Ensure active standings read official override values where present.
- Keep imported and recalculated values queryable for audit and comparison.

Output:
- Active leaderboard matches official standings.
- Participant detail exposes imported, recalculated, and effective displayed values.

Exit criteria:
- All validated official corrections are represented as override data, not ad hoc display hacks.
- Active leaderboard parity with official standings is achieved.

### Chunk 4 - Admin Reconciliation Workflow Hardening
Goal: make reconciliation repeatable and safe.

Work:
- Standardize expected variance reason categories.
- Confirm migration kickoff, export, and rollback flows for reconciliation cycles.
- Confirm traceability from run id to score differences.

Output:
- Repeatable reconciliation process with auditable evidence.
- Reliable rollback path for correction iterations.

Exit criteria:
- Admin can complete a full reconcile cycle without manual database edits.
- Rollback and rerun process is validated.

### Chunk 5 - Phil 2025 Signoff
Goal: lock confidence for production use.

Work:
- Final parity check of active standings versus official table.
- Validate participant detail explanations for major delta cohorts.
- Freeze known overrides and reconciliation notes.

Output:
- Phil 2025 migration signoff package.

Exit criteria:
- Stakeholder confirmation that active standings are officially correct.
- No open critical mismatch issues.

## What Is Already Working
The API currently has working foundations for Phil 2025 migration and validation:
- Competition leaderboard endpoint with active and compare views.
- Participant level score breakdown across race picks, preseason questions, and H2H.
- Admin migration run management endpoints including list, detail, kickoff, rollback, and exports.
- Canonical race context and race metadata endpoints.

Done status against chunks:
- Chunk 1: partial complete.
- Chunk 2: partial complete.
- Chunk 3: in progress and already supported in active scoring behavior.
- Chunk 4: partial complete.
- Chunk 5: not started.

## Official Score Alignment Requirement
The legacy competition includes known historical miscalculations.

Requirement:
Displayed official standings must stay aligned to the agreed official competition totals, even when pure recalculation differs.

Implementation approach in current API behavior:
- Store calculated points from canonical rules.
- Support an override score per score row when an official adjustment is required.
- Use override score for active standings display when the competition score source policy is legacy official mode.
- Keep imported and recalculated values visible for admin comparison and audit.

Operational rule:
- If OverrideScore exists for a row, it is the effective official score contribution for active standings.
- If no OverrideScore exists, use CalculatedPoints as the effective score contribution.
- ImportedPoints remains historical reference only unless admin selects compare imported view.

This ensures:
- entrants see official standings,
- admins can still inspect differences and correction impact,
- migration can proceed without losing transparency.

## Working Definition of Done For Phil 2025 Migration
Phil 2025 migration is considered done when:
- leaderboard output for active mode matches official published standings,
- participant detail explains imported, recalculated, and delta behavior clearly,
- all known legacy miscalculation cases are represented through explicit override rows,
- admin compare mode can still switch to imported and recalculated views,
- migration run evidence exists for traceability and rollback remains available.

## In Progress
- Continue reconciling remaining point deltas for Phil 2025 participants.
- Record each validated official correction as a score override instead of ad hoc manual display logic.
- Tighten explanatory reason codes for mismatch categories so admin review is faster.

Current active chunk:
- Chunk 3 - Official Score Override Layer.

Immediate next tasks:
- Finalize list of validated legacy miscalculation cases.
- Ensure each case is represented by override-backed score records.
- Re-run admin comparison and confirm active leaderboard parity.

## Next After Phil 2025 Stabilization
- Reuse the same pattern for additional competitions.
- Introduce richer per race dynamic bonus question authoring once Phil 2025 data confidence is complete.

## Deferred Until Phil 2025 Is Signed Off
- Dave 2025 additional per-race bonus question model rollout.
- Dave 2026 admin-authored dynamic race question workflows.
- Generic question authoring UI and expanded API endpoints beyond Phil 2025 migration needs.
