# Epic E: Post-Login User Journey and Competition Workspace

## Summary
Design and implement the post-login journey so users land in the right competition context, review standings clearly, and drill down into participant selections and migration context.

## Why This Epic
Users need a predictable flow after login. Today, context and review goals are not explicit enough for fast navigation and reliable decision-making.

## Goals
- Provide role-aware landing behavior after authentication.
- Make competition-season context selection explicit and persistent.
- Expose score-source clarity in leaderboard and detail views.
- Improve deep-link and workflow continuity for admin review.

## Non-Goals
- Changing migration scoring math.
- Rebuilding unrelated site navigation.

## Product Questions to Resolve
- Should non-admin users land on last-used competition context or a global home page by default?
- Should competition-season selection be mandatory before any leaderboard data is shown?
- Should admin users land in migration operations context when pending approvals exist?
- How should stale deep links behave when the referenced run or participant is no longer available?

## User Stories

### Story E1: Define role-based landing page after login
As a user, I want a role-appropriate landing destination so I can start in the right workspace.

Acceptance criteria:
- Post-login routing resolves destination based on role and account state using a deterministic rule set.
- Admin and non-admin landing behavior is documented and configurable.
- Fallback route exists for unknown or partially configured contexts.

Test notes:
- Add routing tests for each role and fallback condition.
- Add regression test ensuring unauthorized users cannot access admin landing routes.

### Story E2: Add competition and season selector with last-used context
As a frequent user, I want context recall so repeated visits are faster.

Acceptance criteria:
- Selector supports available competitions and seasons with clear current selection state.
- Last-used context is persisted and restored when still valid for user permissions.
- Invalid or removed context falls back safely to a valid selectable default.

Test notes:
- Add integration tests for context persistence and restore behavior.
- Add edge-case tests for deleted or unauthorized previously saved context.

### Story E3: Add competition leaderboard with clear score-source labeling
As a participant, I want transparent score-source labels so standings interpretation is unambiguous.

Acceptance criteria:
- Leaderboard displays active score totals and visible score-source label per page context.
- Label semantics align exactly with scoring truth contract from GH-285.
- Sorting and tie-break behavior is deterministic and documented.

Test notes:
- Add UI tests validating score-source labels and helper text presence.
- Add API/UI parity tests for standings order and tie-break consistency.

### Story E4: Add participant drilldown into race picks, preseason, and H2H
As a reviewer, I want per-person drilldown so totals can be validated against component outcomes.

Acceptance criteria:
- Participant detail includes segmented views for race picks, preseason questions, and H2H questions where data exists.
- Drilldown shows component totals and links back to parent competition-season context.
- Missing component data is handled with explicit empty states, not hidden failures.

Test notes:
- Add component integration tests for full-data and partial-data participant scenarios.
- Add E2E test covering navigation from leaderboard to participant drilldown and back.

### Story E5: Add migration-run visibility and status chips in admin journey
As an admin, I want migration status visibility in my normal flow so operational health is obvious.

Acceptance criteria:
- Admin workspace surfaces recent migration run status chips with consistent status taxonomy.
- Chip states include links to run detail or review workflow where appropriate.
- Visibility rules ensure only authorized users can see operational run metadata.

Test notes:
- Add UI tests for status chip mapping by run state.
- Add authorization tests ensuring non-admin paths do not expose migration metadata.

### Story E6: Add shareable deep links to competition, participant, and run context
As an operator, I want URL-stable links so reviews can be shared without losing state.

Acceptance criteria:
- URL query/path state captures competition, season, participant, active tab/section, and run context when applicable.
- Reload restores equivalent context without requiring additional manual selection.
- Invalid deep-link parameters are handled gracefully with user-visible recovery behavior.

Test notes:
- Add deep-link tests for direct navigation, reload, and context restoration.
- Add negative tests for malformed or stale parameters with graceful fallback assertions.

### Story E7: Add end-to-end accessibility and keyboard flow for review workflows
As a keyboard and screen-reader user, I want full workflow coverage so review actions are accessible.

Acceptance criteria:
- Core flows (landing, selector, leaderboard, drilldown, run visibility, deep-link navigation) are keyboard-operable.
- Semantics include required landmarks, labels, focus states, and tab order consistency.
- Accessibility audit findings for critical severity are resolved before release.

Test notes:
- Add automated accessibility checks for key pages and components.
- Add keyboard-navigation E2E tests for primary review workflow paths.

### Story E8: Separate migration storage from main domain data
As an engineer, I want migration-prefixed tables isolated from the main application data model so admin migration workflows do not leak into product read paths and the final data is persisted in the correct canonical tables.

Acceptance criteria:
- Any table or entity whose name starts with `Migration` or `MigrationImport` is only used by migration/admin workflows, import staging, reconciliation, or audit views.
- Core product features read from canonical domain tables only and do not depend on migration-prefixed tables for runtime behavior.
- The migration pipeline explicitly moves or materializes the needed data into the proper canonical tables before the app or UI consumes it.
- If the implementation chooses a separate schema or separate database, the boundary is documented and enforced so migration storage and canonical storage cannot be mixed accidentally.
- The storage model includes an explicit mapping from migration tables to their canonical target tables for every persisted data type that needs to survive import.
- Idempotent re-run behavior is preserved so imports can be repeated without duplicating canonical data or leaving orphaned migration records.

Test notes:
- Add architecture-level tests or static checks that fail if non-admin code paths reference `Migration*` tables directly.
- Add import/integration tests proving the required data lands in canonical tables after migration completes.
- Add rerun/idempotency tests for the migration-to-canonical handoff.

## Delivery Plan
1. Finalize role-based post-login destinations and context contract
2. Implement competition-season selection and remembered context
3. Deliver leaderboard and participant drilldown with score-source clarity
4. Add deep-linking, admin migration visibility, and accessibility coverage
5. Define and enforce the migration-storage boundary between admin/import tables and canonical application data

## Risks and Mitigations
- Risk: Users lose context when navigating between leaderboard and participant detail.
- Mitigation: Persist selected competition/season in URL and last-used context storage.

- Risk: Score-source labels are present but still misunderstood.
- Mitigation: Use consistent labels, helper copy, and link to scoring truth contract.

- Risk: Migration-prefixed tables become accidental runtime dependencies.
- Mitigation: Keep migration data isolated to admin/import workflows, enforce a canonical target mapping, and consider a dedicated schema or database boundary if table-level separation is not enough.

## Definition of Done
- Post-login flow is role-based and deterministic.
- Competition-season context is selectable, persisted, and deep-linkable.
- Leaderboard and participant drilldowns show clear score-source semantics.
- Accessibility and keyboard flow coverage exists for primary review tasks.
- Migration-prefixed tables are isolated from main product reads and only feed canonical domain tables through explicit import steps.
