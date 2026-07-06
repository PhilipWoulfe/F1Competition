# Epic: Expected Variance Rules V2 - DB Backed Governance and UI Authoring

## Summary
Move expected variance rule management from manifest-only promotion to a DB-backed operating model with strong governance, auditable history, and operator-first tooling.

This epic keeps the current manifest approach as a stable baseline while introducing:
- Rule persistence in Postgres with environment scope and validity windows
- Idempotent promotion and sync from source-controlled artifacts
- Resolver preference for DB rules with controlled manifest fallback
- Admin UI workflows for filtering, ordering, selecting reconciliation rows, and creating rules from real diffs

## Why This Epic
Current JSON/manifest behavior is intentionally lightweight and safe. It solved immediate promotion and reproducibility needs.

V2 is needed to improve day-2 operations:
- Operators need low-friction rule lifecycle management without full redeploys
- Reviewers need richer audit trails and queryability over rule history
- Governance needs structured approval, ownership, expiration, and rollback controls
- UI needs row-to-rule authoring to reduce operator error and speed triage

## Goals
- Preserve immutable reconciliation facts (imported, calculated, delta)
- Add first-class DB lifecycle management for expected variance rules
- Keep promotions deterministic and idempotent across dev, test, and prod
- Provide auditable rule change history and per-run applied ruleset evidence
- Enable operators to author rules directly from selected diff rows in admin UI

## Non-Goals
- Replacing immutable diff storage with mutable corrected values
- Allowing rules to overwrite imported or calculated points
- Removing manifest support immediately (fallback remains during migration)

## Target Architecture (V2)
- Source of truth: DB tables for active rules and rule revisions
- Promotion source: source-controlled rule bundles applied by idempotent upsert job
- Runtime resolver: DB-first, manifest fallback behind feature flag
- Audit: per-run applied rule-set version/checksum plus who/when metadata for rule changes
- UI: operator rule console with filter/sort/create-from-row/review/expire actions

## Data Model Direction
Proposed tables:
- `ExpectedVarianceRules`
  - `RuleId` (stable external id)
  - `ReasonCode`
  - `Subject`, `RaceCode`, `PickType`
  - `ImportedSourcePattern`, `CalculatedSourcePattern`
  - `EnvironmentScope` (single or multi-env)
  - `Owner`, `Rationale`, `ReferenceLink`
  - `EffectiveFromUtc`, `ExpiresAtUtc`
  - `Status` (Draft, Approved, Active, Expired, Revoked)
- `ExpectedVarianceRuleRevisions`
  - full change history per rule with author, reviewer, timestamp, change reason
- `ExpectedVariancePromotionEvents`
  - promotion batch id, source version/checksum, target environment, result

## User Stories

### Story 1: Persist expected variance rules in DB
As an operator, I want expected variance rules stored in Postgres so they can be managed and queried without relying only on file edits.

Acceptance criteria:
- Rules are stored in normalized DB tables with unique constraints by rule id and revision.
- Schema supports owner, rationale, reference, effective date, and expiration.
- Data access layer prevents duplicate active rule records for the same revision.

### Story 2: Idempotent promotion/upsert from source bundles
As an operator, I want rule promotions to be repeatable so applying the same package twice does not duplicate or drift data.

Acceptance criteria:
- Promotion command/job performs deterministic upsert by rule id + version.
- Reapplying identical payload results in no duplicate rows and no changed checksum.
- Promotion events are recorded with batch id and outcome.

### Story 3: Runtime resolver with DB preference and manifest fallback
As a platform engineer, I want runtime classification to prefer DB rules while retaining manifest fallback for safety during migration.

Acceptance criteria:
- Resolver checks active DB rules first.
- Optional fallback to manifest is feature-flag controlled.
- Applied source (DB or manifest), ruleset id, version, and checksum are logged per run.

### Story 4: Admin API for rule listing with filtering and ordering
As an admin, I want to list and inspect rules with filters and ordering so I can quickly find what is active and why.

Acceptance criteria:
- API supports filters: environment, status, owner, reason code, race code, subject, expiring soon.
- API supports ordering: updated desc, created desc, expires asc, owner asc, rule id asc.
- API supports pagination with stable deterministic ordering.

### Story 5: Admin UI filtering and ordering for rules
As an operator, I want UI controls for filtering and sorting rules so governance reviews are fast.

Acceptance criteria:
- Rules screen exposes the same filter and sort options as API.
- Current filter/sort state is preserved in query string for shareable review links.
- Expiring/expired rules are visually highlighted.

### Story 6: Select reconciliation row and create rule draft in UI
As an operator, I want to select a diff row and generate a prefilled rule draft so I can author expected variances accurately.

Acceptance criteria:
- In run detail tables, each diff row has an action to create expected variance rule draft.
- Draft is prefilled from selected row fields: subject, race code, pick type, source references, reason.
- Operator must supply owner, rationale, reference, environment scope, and expiration before save.
- Saving draft does not modify existing diff facts.

### Story 7: Rule review and approval workflow
As a team lead, I want a review/approval step so only vetted rules become active.

Acceptance criteria:
- Draft rules require explicit approval before activation.
- Approval captures reviewer identity and timestamp.
- Rejection requires comment and keeps immutable audit trail.

### Story 8: Rule expiration and renewal controls
As an operator, I want expiring rule workflows so stale assumptions do not silently persist.

Acceptance criteria:
- Expiring soon and expired filters are available in API/UI.
- Renewal creates a new revision rather than mutating history.
- Expired rules are excluded from runtime matching unless re-approved.

### Story 9: End-to-end audit and traceability
As a reviewer, I want to confirm exactly which rule set classified a run so sign-off remains defensible.

Acceptance criteria:
- Run detail includes applied ruleset source, version, checksum, and active rule count.
- Rule drill-down links from run diff rows to active rule revision.
- Export includes expected status, expected reason, rule id, and rule revision id when available.

### Story 10: Rollback and safety controls
As an operator, I want safe rollback options for misclassified rules so production triage remains reliable.

Acceptance criteria:
- Rule can be revoked without deleting revision history.
- Emergency switch can disable DB rule activation and use manifest-only fallback.
- Rollback event is logged with actor, reason, and affected rule ids.

## Additional Improvements (Recommended Backlog)
- Rule simulation mode: preview match impact before activation.
- Rule conflict detection: detect overlapping/broad patterns and require explicit override.
- Drift monitor: alert when dev/test/prod active checksums diverge unexpectedly.
- Bulk import/export tooling: CSV/JSON package support with schema validation.
- SLA metrics dashboard: time-to-approve, active rule count, expired rule count, unexpected delta trend.
- Sensitive data guardrails: validate and reject rules containing secrets or unsafe patterns in rationale/reference fields.

## Delivery Plan
1. DB schema and repository layer
2. Idempotent promotion/upsert job and promotion audit table
3. Runtime resolver DB-first with fallback feature flag
4. Admin API list/filter/order plus rule detail endpoints
5. Admin UI rules console and row-to-rule draft flow
6. Review/approval/expiration workflows and notifications
7. Run-detail traceability surfaces and export enrichment
8. Rollback runbook updates and operational drills

## Risks and Mitigations
- Risk: Over-broad rules hide real regressions.
- Mitigation: Narrow matching defaults, conflict checks, and mandatory review workflow.

- Risk: Environment drift during staged rollout.
- Mitigation: Promotion events + checksum comparison endpoint and alerts.

- Risk: Operational complexity increases.
- Mitigation: Keep manifest fallback and release feature flags for phased adoption.

## Definition of Done
- DB-backed rules are active in runtime classification behind controlled feature flags.
- Promotion is idempotent and auditable across environments.
- Admin UI supports filter/order and create-rule-from-selected-diff-row workflow.
- Rule governance workflow (draft, approve, expire, revoke) is implemented and documented.
- Run detail and exports expose expected variance traceability to rule and revision.