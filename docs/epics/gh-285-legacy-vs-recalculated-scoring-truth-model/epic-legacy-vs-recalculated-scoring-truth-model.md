# Epic D: Legacy vs Recalculated Scoring Truth Model

## Summary
Define and implement a clear scoring truth model so migrated data can preserve legacy values while supporting recalculated accuracy with explicit governance and auditability.

## Why This Epic
Migration workflows currently need both historical and corrected score views. Without an explicit truth contract, standings, UI totals, and exports can become inconsistent.

## Goals
- Preserve both imported legacy scores and recalculated scores.
- Define one canonical source for active standings.
- Add traceable reconciliation and sign-off controls.
- Ensure exports and API payloads expose score source decisions.

## Non-Goals
- Replacing scoring rule implementation in this phase.
- Removing legacy score visibility from migration audit surfaces.

## Product Questions to Resolve
- Which score drives official standings after migration sign-off?
- Does activation require explicit approval or happen at write completion?
- Can users switch score view mode, and if so, where is default defined?
- How are disagreements between legacy and recalculated totals communicated?

## User Stories

### Story D1: Define canonical scoring truth contract for migrated records
As a product owner, I want a written scoring truth contract so all runtime and review surfaces follow the same rules.

Acceptance criteria:
- Scoring truth contract defines the meaning of imported score, recalculated score, and active score source.
- Contract specifies allowed score-source transitions and required approvals for each transition.
- API and UI terminology for score source is standardized and documented.

Test notes:
- Add contract tests to validate payload fields and enum values for score-source metadata.
- Add documentation check in CI (or review checklist) confirming contract references are updated when schema changes.

### Story D2: Persist legacy imported score alongside recalculated score
As an engineer, I want both score channels persisted so we never lose historical context.

Acceptance criteria:
- Persistence layer stores imported and recalculated values side by side at required granularity (run, participant, scope).
- Existing data migrations preserve legacy values without overwriting recalculated values and vice versa.
- Data access APIs return both score channels and source metadata in a single query shape.

Test notes:
- Add integration tests verifying both score channels are persisted and retrievable for the same entity.
- Add migration tests proving backfill or upgrade paths do not drop or mutate legacy score data.

### Story D3: Define which score drives standings and UI totals
As an operator, I want one active score source selected for standings so user-visible totals are deterministic.

Acceptance criteria:
- One active score source is selected per competition-season context using explicit configuration or workflow state.
- Standings and totals always resolve from the active source and expose that source in metadata.
- Source selection changes are versioned and auditable, with effective timestamp.

Test notes:
- Add unit tests for standings resolver across all supported source modes.
- Add integration tests proving UI and API totals switch consistently when active source changes.

### Story D4: Add reconciliation status and sign-off state machine
As a reviewer, I want reconciliation states and sign-off transitions so activation decisions are auditable.

Acceptance criteria:
- State machine includes explicit states (for example Draft, InReview, Approved, Active, Rejected, Revoked) with transition guards.
- Transition actions capture actor, timestamp, and reason/comment.
- Invalid transitions are blocked and return clear operator-facing diagnostics.

Test notes:
- Add state-machine tests for valid/invalid transitions and guard conditions.
- Add audit log tests verifying transition metadata is written for every state change.

### Story D5: Add operator override/governance path for exceptional legacy handling
As a team lead, I want controlled override paths for edge cases so exceptions are handled without data tampering.

Acceptance criteria:
- Override workflow requires explicit rationale, actor identity, and approval policy before becoming effective.
- Overrides do not mutate raw imported or recalculated values; they change active interpretation only.
- Override scope is bounded (competition, season, participant, or run) and expires or is revocable.

Test notes:
- Add workflow tests for override create, approve, apply, revoke lifecycle.
- Add regression tests verifying raw score channels remain unchanged when override is active.

### Story D6: Add audit fields and exports that expose both values and chosen truth source
As an auditor, I want exports to include both score values and active source metadata so decisions are defensible.

Acceptance criteria:
- Export payloads include imported score, recalculated score, active score source, and relevant run/sign-off identifiers.
- Audit fields include actor, action, reason, timestamps, and version/checksum references where applicable.
- API and export output ordering is deterministic for repeatable audit comparison.

Test notes:
- Add export schema tests for required audit and score-source fields.
- Add deterministic output tests ensuring repeated exports of unchanged data are byte-stable or field-stable by contract.

## Delivery Plan
1. Finalize score-source truth contract and sign-off semantics
2. Implement dual-score persistence and active-source selection
3. Add reconciliation state machine and operator override governance
4. Expose truth-source metadata in API, UI, and exports

## Risks and Mitigations
- Risk: Users misinterpret active scores when both values are visible.
- Mitigation: Strong source labels, tooltips, and explicit active-source metadata.

- Risk: Activation workflow introduces operational friction.
- Mitigation: Keep controls explicit but minimal and align with runbooked sign-off.

## Definition of Done
- Dual score channels are persisted and queryable.
- Active standings source is explicit and consistent across API/UI/exports.
- Sign-off and override actions are auditable with actor and timestamp metadata.
