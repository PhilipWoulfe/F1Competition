# Epic: Migration Write Correctness and Non-Empty Database Safety

## Summary
Fix write-mode migration so it persists to the intended domain tables, behaves deterministically on rerun, and safely handles non-empty databases without hidden data loss or duplication.

## Why This Epic
Current behavior indicates write mode does not materialize data into the expected tables. This is a release blocker.

Non-empty database handling is also undefined. Without a clear contract, reruns can create duplicate records, overwrite valid state, or produce ambiguous review output.

## Goals
- Ensure write mode persists validated migration output to canonical target tables.
- Guarantee dry-run and write-run parity for validation and reconciliation logic.
- Define and enforce non-empty database behavior for append, merge, or replace policies.
- Preserve idempotency by source checksum and run scope.

## Non-Goals
- Reworking scoring formulas in this epic.
- Redesigning the entire migration UX.
- Supporting every historical source format immediately.

## Decision Topics
- Non-empty DB mode contract:
  - Append-only snapshots
  - Merge/upsert into active records
  - Replace within run-scoped partition
- Conflict policy for overlapping participant/race/question records.
- Activation model: write immediately active versus write then explicit promote.

## Product Questions to Resolve
- Should non-empty DB behavior be one global default or selectable per run with approval guardrails?
- If incoming data conflicts with active canonical rows, should write mode fail hard or continue with partial conflict skips?
- Should write mode always create a new immutable run snapshot even when merge/upsert is selected?
- What is the operator-facing rollback SLA target after a failed or incorrect write run?

## User Stories

### Story 1: Trace and document current write-run pipeline gaps
As an engineer, I want an implementation-level trace so no-op write points and table mismatches are explicit.

Acceptance criteria:
- End-to-end write pipeline map documents each stage from kickoff to persistence, including table-level targets.
- Current no-op behavior is reproduced and traced with concrete evidence (logs, call graph, or failing integration test).
- Gap report identifies missing repository calls, transaction boundaries, and write guards blocking persistence.

Test notes:
- Add a characterization test that reproduces current failed/no-op write behavior before fix.
- Add traceability artifact reference in docs/runbook so future regressions can be triaged quickly.

### Story 2: Implement transactional write path to canonical tables
As an operator, I want write runs to persist all intended entities atomically so partial writes cannot corrupt state.

Acceptance criteria:
- Write mode persists to canonical target tables for participant, race, pick, and question entities where applicable.
- All persistence for one run is wrapped in transactional boundaries with rollback on unrecoverable failures.
- Partial-write states are prevented or explicitly marked as failed and non-active.

Test notes:
- Add integration tests asserting committed rows exist in all expected tables after successful write.
- Add fault-injection test proving transaction rollback on mid-pipeline persistence failure.

### Story 3: Enforce dry-run and write-run parity
As a reviewer, I want the same validation and reconciliation outcomes regardless of mode so sign-off is trustworthy.

Acceptance criteria:
- Validation, normalization, reconciliation, and summary outputs match between dry-run and write-run for identical inputs.
- Any intentional behavioral differences between modes are documented and limited to persistence side effects.
- Run reports expose parity status and checksum comparison between modes.

Test notes:
- Add paired-run tests executing dry-run and write-run on same source and diffing outputs.
- Add regression test for deterministic ordering and aggregate totals parity.

### Story 4: Add idempotent upsert semantics for reruns
As a platform maintainer, I want repeat writes of the same source checksum to avoid duplicates and drift.

Acceptance criteria:
- Re-running identical source checksum does not create duplicate domain rows.
- Re-run behavior is deterministic and records idempotent outcome in run metadata.
- Idempotency keys include source checksum and run scope dimensions required by policy.

Test notes:
- Add run-twice integration tests verifying row counts and checksums remain stable.
- Add negative tests ensuring changed checksum correctly produces new or updated data per policy.

### Story 5: Define and enforce non-empty database strategy
As a team lead, I want an explicit policy for existing data so migrations are predictable in live-like environments.

Acceptance criteria:
- One approved non-empty DB strategy is documented and enforced in code paths.
- Strategy behavior is explicit for existing active data, historical snapshots, and conflicts.
- Operator confirmation includes strategy preview and expected affected entity counts.

Test notes:
- Add integration tests on seeded non-empty DB fixtures covering approved strategy behavior.
- Add runbook validation tests or checklist proving operator workflow matches implementation.

### Story 6: Add duplicate detection and conflict reporting
As an operator, I want clear conflict diagnostics when incoming migration rows overlap existing canonical rows.

Acceptance criteria:
- Conflicts and potential duplicates are detected pre-commit and surfaced in run summary.
- Diagnostics include entity type, key fields, source references, and recommended next action.
- Conflict outcomes are policy-driven (fail, skip, or override) and auditable.

Test notes:
- Add conflict fixture tests for participant/race/pick/question overlap combinations.
- Add API/UI contract tests ensuring conflict diagnostics are visible to admins.

### Story 7: Add safe rollback and compensation for failed writes
As an operator, I want recovery procedures for failed write runs so bad data can be reverted quickly.

Acceptance criteria:
- Failed writes are recoverable through automated rollback or documented compensation flow.
- Rollback/revoke operations capture actor, reason, timestamp, and affected run/entity scope.
- Recovery actions preserve immutable audit history and do not delete run evidence.

Test notes:
- Add rollback integration test proving post-recovery state matches pre-run baseline.
- Add audit trail tests confirming rollback metadata is persisted and queryable.

### Story 8: Add integration tests for write correctness on empty and non-empty DBs
As a QA engineer, I want automated proof that write mode behaves correctly across both scenarios.

Acceptance criteria:
- Test suite covers empty DB, non-empty DB, rerun-idempotent, conflict, and rollback scenarios.
- CI pipeline includes these tests in a stable, repeatable target.
- Test output includes row count/checksum assertions for key canonical tables.

Test notes:
- Add dedicated fixtures for empty baseline and representative non-empty production-like state.
- Add CI reporting for flaky-test detection and deterministic rerun validation.

## Delivery Plan
1. Pipeline trace and target-table mapping
2. Transactional write implementation
3. Non-empty DB policy and conflict handling
4. Idempotency and rollback mechanics
5. Integration and end-to-end validation

## Risks and Mitigations
- Risk: Hidden legacy assumptions break when enforcing canonical writes.
- Mitigation: Introduce migration compatibility checks and staged rollout flags.

- Risk: Non-empty DB policy causes unintended user-visible changes.
- Mitigation: Require explicit mode selection with confirmation and run preview.

## Definition of Done
- Write mode persists to the correct domain tables with transactional integrity.
- Rerunning the same source does not duplicate or drift persisted data.
- Non-empty database behavior is documented, implemented, and tested.
- Rollback/recovery procedures are runbooked and validated.
