# Epic: GH-270 Migration Reconciliation Hardening and Expected Variance Classification

## Summary
Harden the Phil 2025 migration reconciliation flow so reviewers can quickly distinguish true defects from known legacy data errors without hiding any raw deltas.

This epic builds on the existing migration pipeline by improving:
- Deterministic ordering of reconciliation output by race occurrence
- Source-provenance explainability for imported versus calculated values
- Contract-scoped typo normalization for known Phil 2025 anomalies
- Classification of expected legacy variances for faster review and sign-off

## Problem Statement
Migration runs currently surface all variances together, including known legacy mistakes in the original spreadsheet. This creates noisy review output and slows triage.

Primary risks:
- Reviewers cannot quickly separate expected legacy errors from unexpected regressions
- Explanation output is easy to misread without explicit source-section context
- Known source typos can generate false-positive variances
- Output ordering that does not follow source race order increases cognitive load in audits

## Goals
- Preserve all imported, calculated, and delta values as immutable reconciliation facts
- Classify known legacy discrepancies as expected without suppressing them
- Improve explanation clarity so row and column references are immediately understandable
- Keep reconciliation output deterministic and aligned to race occurrence order

## Scope
In scope:
- Reconciliation output ordering by race occurrence sequence
- Explanation text labeling for race-picks versus race-points source rows
- Phil 2025 contract-scoped typo normalization needed for scoring parity
- Expected variance classification model and matching rules
- Admin/API filtering and summaries for expected versus unexpected variances

Out of scope:
- Retroactively rewriting imported legacy points or source CSV files
- Suppressing or deleting expected variances from persisted reconciliation entities
- Redesigning the core podium or DNF scoring model

## Canonical Contract (Target State)
- Reconciliation values remain immutable: imported, calculated, delta are always preserved.
- Expected variance is metadata layered on top of the diff, not a value override.
- Expected variance matching is deterministic and traceable to a rule identifier.
- Reviewer summaries report both total variance and unexpected-only variance.

## User Stories
### Story 1: Order reconciliation output by race occurrence
As a migration reviewer, I want comparisons listed in race occurrence order so I can verify output against the source CSV with minimal context switching.

Acceptance criteria:
- Pick-level comparisons are sorted by first-seen race occurrence, then subject, then pick type.
- Race-level comparisons are sorted by first-seen race occurrence, then subject.
- Ordering is deterministic across reruns of the same source.

### Story 2: Label explanation source provenance
As an admin, I want explanation text to explicitly label source sections so row references are unambiguous.

Acceptance criteria:
- Imported references are labeled as race-points source rows.
- Calculated references are labeled as race-picks source rows.
- Existing row and column references remain intact.

### Story 3: Normalize known Phil 2025 typo tokens
As an operator, I want known typo tokens normalized under the Phil contract so expected matches are not reported as false variances.

Acceptance criteria:
- Known typo token corrections are contract-scoped to Phil 2025 inputs.
- Corrections apply only to intended pick types and do not alter unrelated behavior.
- Regression tests cover corrected typo scenarios.

### Story 4: Add expected variance classification metadata
As a reviewer, I want to mark differences as expected when caused by known legacy data errors.

Acceptance criteria:
- Diff entities support metadata fields such as expected flag, reason code, and rule id.
- Classification does not change imported, calculated, or delta values.
- Rules can target subject, race code, pick type, and source row/column patterns.

### Story 5: Report unexpected-only summaries
As a team lead, I want unexpected-only aggregates so release decisions focus on unresolved discrepancies.

Acceptance criteria:
- Summaries include total delta across all variances.
- Summaries include total delta across unexpected variances only.
- Admin/API responses support filtering by expected status.

### Story 6: Provide operator runbook for expected variance governance
As an operator, I want a documented process for creating and maintaining expected variance rules.

Acceptance criteria:
- Runbook defines rule authoring, review, approval, and expiration policy.
- Each rule includes owner, rationale, and traceable reference.
- Runbook includes rollback/removal steps for incorrect rules.

### Story 7: Promote expected variance rules across environments
As an operator, I want expected variance rules to move consistently from dev to test and prod so I can compare the same classifications in each environment.

Acceptance criteria:
- Rules are stored in an idempotent source of truth that can be applied repeatedly without duplication.
- The same approved rule set can be promoted from dev to test and prod with environment-specific targeting.
- Rule promotion is auditable so reviewers can confirm which environment received which rule version.
- Environment sync does not alter imported, calculated, or delta values.

## Delivery Plan
1. Finalize expected variance data model and matching contract
2. Implement rule evaluation in reconciliation service
3. Add expected/unexpected filtering and aggregate summaries
4. Update admin review UX and API payloads
5. Publish runbook and onboarding checklist for rule governance
6. Add environment-safe promotion and seeding for expected variance rules

## Risks and Mitigations
- Risk: Expected flags hide true regressions.
- Mitigation: Preserve immutable deltas and report unexpected-only metrics separately.

- Risk: Rule set grows without governance.
- Mitigation: Require owner, rationale, and periodic rule review.

- Risk: Over-broad rules misclassify valid discrepancies.
- Mitigation: Favor narrow matching keys and add regression tests per rule pattern.

## Definition of Done
- Reconciliation ordering follows race occurrence order deterministically.
- Explanation text clearly distinguishes race-picks and race-points source references.
- Known Phil 2025 typo mismatches are normalized as specified.
- Expected variance metadata is implemented and queryable.
- Admin/API supports expected-status filtering and unexpected-only summaries.
- Runbook exists for expected variance lifecycle management.
