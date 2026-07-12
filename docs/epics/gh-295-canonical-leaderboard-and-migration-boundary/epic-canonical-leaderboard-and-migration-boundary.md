# Epic GH-295: Canonical Leaderboard and Migration Boundary

## Summary
Separate migration operations from product runtime data paths so the main user leaderboard is powered only by canonical tables and migration comparison views live only in admin migration workspace.

This epic carries forward incomplete scope from GH-286 Story E8 and Story E9, and adds explicit UX separation between admin migration leaderboard context and main leaderboard context.

## Problem Statement
- Migration and canonical concepts are still mixed in product language and navigation.
- Main leaderboard UX and migration-review UX are not clearly separated.
- Teams need an explicit way to compare migration-derived results against canonical runtime leaderboard to detect missing functionality and parity gaps.

## Goals
- Keep all runtime product reads on canonical tables and APIs.
- Keep migration-prefixed storage and APIs inside admin migration workflows.
- Move current migration comparison leaderboard display into admin migration workspace.
- Provide a distinct main leaderboard tab/page that reads only canonical runtime endpoints.
- Add a side-by-side comparison workflow that highlights gaps between admin migration view and canonical runtime view.

## Hard Invariant
- Any table, entity, DTO, endpoint, or service with `Migration` or `MigrationImport` in the name is forbidden in main product functionality.
- Allowed scope for migration-prefixed assets is admin/import/reconciliation/audit only.
- Main functionality includes post-login user journey, non-admin runtime endpoints, and shared leaderboard UX.

## Non-Goals
- Rewriting migration scoring logic.
- Removing migration audit/history tables.
- Changing unrelated post-login flows outside leaderboard and migration workspace boundaries.

## Table Rationalization (Canonical Runtime)
These tables are canonical product-domain tables and must remain runtime-safe:

1. public.QuestionTemplates
- Purpose: question definitions per competition/season/category.
- Runtime usage: question metadata for preseason and H2H.
- Write paths: admin authoring or migration handoff to canonical.

2. public.QuestionAnswers
- Purpose: participant-submitted or imported answer values.
- Runtime usage: optional review surfaces and recalculation workflows.
- Write paths: participant submissions, admin edits, migration handoff.

3. public.QuestionActuals
- Purpose: resolved actual outcomes for scored questions.
- Runtime usage: explanation and recompute sources.
- Write paths: admin outcome entry and migration handoff.

4. public.QuestionScores
- Purpose: canonical persisted question scoring artifacts.
- Runtime usage: leaderboard and participant detail scoring reads.
- Write paths: canonical scoring pipeline and migration canonical write stage.

5. public.Races
- Purpose: competition race schedule and identity.
- Runtime usage: race context, joins, ordering.
- Write paths: schedule sync and admin maintenance.

6. public.RaceMetadata
- Purpose: per-race metadata such as bonus/H2H prompts.
- Runtime usage: race question rendering and context.
- Write paths: admin or import materialization.

7. public.Selections
- Purpose: participant race selection headers.
- Runtime usage: selection lifecycle and ownership context.
- Write paths: participant picks and migration canonical write.

8. public.SelectionPositions
- Purpose: ordered driver selections tied to selection header.
- Runtime usage: pick rendering and scoring inputs.
- Write paths: participant picks and migration canonical write.

Boundary rule:
- Any table prefixed with Migration or MigrationImport is admin/import/reconciliation/audit only.
- Product runtime endpoints must not read migration-prefixed tables.

Runtime usage rule:
- Main pages/components must not call migration-admin APIs, even for convenience badges/strips.
- Migration run visibility belongs only in admin migration workspace.

## User Stories

### Story N1: Finalize and publish canonical-vs-migration data contract
As an engineer, I want an explicit storage contract so table usage cannot drift.

Acceptance criteria:
- A mapping matrix exists from each MigrationImport table to canonical destination or explicit no-handoff classification.
- The eight canonical tables listed in this epic have documented runtime responsibilities.
- Contract is published in runbook and linked from architecture docs.

Test notes:
- Add doc validation in PR checklist requiring mapping updates when entities change.

### Story N2: Move migration leaderboard context into admin migration workspace
As an admin, I want migration comparison leaderboard data in admin migration workspace so operational review is isolated from product runtime views.

Acceptance criteria:
- Admin migration workspace has a dedicated leaderboard tab that displays migration comparison context.
- This view may use migration/admin APIs but is gated to Admin role.
- Main product leaderboard page no longer presents migration comparison-specific language or controls.

Test notes:
- Add route access tests for admin-only migration leaderboard tab.
- Add UI tests validating migration badges and run-context chips stay inside admin workspace.

### Story N3: Create separate main leaderboard tab/page for canonical runtime
As a user, I want a clear main leaderboard page powered by canonical APIs so standings are product-truth and not migration-context dependent.

Acceptance criteria:
- Main leaderboard route/tab reads only canonical runtime endpoints.
- API contract for main leaderboard excludes migration-run identifiers and migration-only concepts.
- Participant drilldown on main leaderboard reads canonical runtime entities only.

Test notes:
- Add API integration tests proving canonical endpoints work after migration staging rows are removed.
- Add UI tests validating non-admin users cannot reach migration comparison UI.

### Story N4: Add canonical vs migration comparison view for gap analysis
As an admin, I want side-by-side comparison between canonical and migration views so missing functionality can be identified and prioritized.

Acceptance criteria:
- Admin comparison view supports same competition/season and participant filter for both sources.
- Deltas are grouped by race picks, preseason, and H2H.
- Missing-field and missing-behavior flags are visible and exportable.

Test notes:
- Add parity tests for matching and divergent datasets.
- Add export tests for CSV/JSON diff output if export is enabled.

### Story N5: Enforce architecture boundaries in CI
As a maintainer, I want guardrails in CI so migration table references cannot leak into runtime product code.

Acceptance criteria:
- Architecture tests fail if non-admin runtime namespaces reference Migration/MigrationImport symbols.
- Guardrails include service and endpoint layers, not only DTOs.
- CI gate includes canonical handoff integration and idempotency checks.

Test notes:
- Extend existing boundary tests with stricter allowlist and coverage for web/runtime namespaces.

### Story N6: Rationalize leaderboard endpoint surface
As a developer, I want explicit API boundaries so consumers know which endpoints are canonical vs migration-admin.

Acceptance criteria:
- Canonical leaderboard endpoints are documented under product API section.
- Migration comparison endpoints are documented under admin API section.
- Endpoint naming and DTOs make data origin explicit.

Test notes:
- Add OpenAPI checks validating tags/grouping and auth requirements.

## Current Status Snapshot (from GH-286 Story E9)
- Implemented:
  - Canonical runtime leaderboard service reads RacePickScores and QuestionScores.
  - API architecture guard exists for migration symbol leakage in non-admin API files.
- Not complete:
  - Migration comparison leaderboard context is not fully isolated to admin migration workspace.
  - Main leaderboard UX still includes admin compare controls rather than a separate canonical-first tab/page.
  - End-to-end comparison workflow for canonical vs migration parity gaps is not first-class yet.

## Known Leakage To Remove
- Main leaderboard page currently injects and calls migration admin API service to render recent migration chips.
- Main leaderboard page links directly to admin migration workspace from the runtime leaderboard surface.

Required fix:
- Remove migration API dependency from main leaderboard page.
- Render migration status only inside admin migration workspace and admin-only tabs there.

## Delivery Plan
1. Publish contract and mapping matrix (N1).
2. Separate UI surfaces: admin migration leaderboard vs main canonical leaderboard (N2, N3).
3. Add explicit comparison workflow for gap analysis (N4).
4. Tighten CI and architecture guardrails (N5).
5. Finalize API surface docs and OpenAPI grouping (N6).

## Definition of Done
- Canonical leaderboard UX is separated from admin migration comparison UX.
- Main leaderboard reads only canonical tables through canonical APIs.
- Migration-prefixed storage is admin/import/reconciliation-only by enforced architecture contract.
- Comparison workflow exists for canonical vs migration parity review.
- CI blocks boundary regressions and validates handoff/idempotency expectations.
- Main leaderboard and other non-admin runtime pages have zero migration-prefixed API/table/service dependencies.
