# Epic: Generic Questions Framework (Preseason, H2H, and Future Variants)

## Summary
Create a reusable question domain and processing framework so preseason questions, head-to-head questions, and future migration-specific question types can be ingested, scored, reconciled, and reviewed using one consistent model.

## Why This Epic
Current migration flow handles position selections but does not provide first-class support for preseason or H2H questions.

Without a generic framework:
- Each new question type requires custom pipeline logic.
- Admin review experiences become fragmented.
- Future migration adapters will duplicate domain and scoring behavior.

## Goals
- Introduce a canonical question model that supports multiple question categories.
- Support ingestion, scoring, and reconciliation for preseason and H2H as first implementations.
- Keep question framework extensible for future migration variants.
- Preserve full traceability from source cell/row to persisted answer and calculated score.

## Non-Goals
- Implementing all future question variants in this phase.
- Replacing race position scoring logic.
- Redesigning non-admin user-facing gameplay UX.

## Target Architecture
- Question template model scoped by competition and season.
- Participant answer model linked to run, template, participant, and source provenance.
- Actual answer model and scoring strategy model by question category.
- Reconciliation model preserving imported score, calculated score, delta, and reason.
- Admin/API views built from the same generic query contract.

## Data Model Direction
Proposed logical entities:
- `QuestionTemplates`
  - `QuestionId`, `CompetitionId`, `Season`, `QuestionCategory`, `Prompt`, `OptionsJson`, `Status`
- `QuestionAnswers`
  - `RunId`, `QuestionId`, `ParticipantId`, `ImportedAnswer`, `NormalizedAnswer`, `SourceRow`, `SourceColumn`
- `QuestionActuals`
  - `RunId`, `QuestionId`, `ActualAnswer`, `ActualSourceRow`, `ActualSourceColumn`
- `QuestionScores`
  - `RunId`, `QuestionId`, `ParticipantId`, `ImportedPoints`, `CalculatedPoints`, `DeltaPoints`, `ReasonCode`

## User Stories

### Story 1: Define canonical question domain model and taxonomy
As an engineer, I want one question model with category taxonomy so preseason and H2H can be represented without special-case tables.

Acceptance criteria:
- Domain contract defines required fields for `QuestionTemplates`, `QuestionAnswers`, `QuestionActuals`, and `QuestionScores`.
- Taxonomy includes at least `Preseason` and `H2H` with explicit extension path for future categories.
- Contract documents which fields are immutable after run completion versus mutable during draft/import stages.

Test notes:
- Add schema/contract tests validating required fields and enum/category constraints.
- Add regression test ensuring adding a new category does not require schema change.

### Story 2: Add question template persistence with competition-season scope
As an operator, I want templates scoped to competition and season so question definitions do not bleed across contexts.

Acceptance criteria:
- Templates are persisted with uniqueness constraints across `CompetitionId`, `Season`, and canonical `QuestionId`.
- Queries can retrieve templates by competition-season context with deterministic ordering.
- Cross-season or cross-competition template leakage is prevented by repository/service validation.

Test notes:
- Add integration tests for insert/update/query by competition and season.
- Add negative test proving same `QuestionId` in a different season does not collide.

### Story 3: Add participant answer persistence with source provenance
As a reviewer, I want persisted source row and column references so answer lineage is auditable.

Acceptance criteria:
- Persist imported and normalized participant answers with source row/column metadata.
- Answer records are linked to run, participant, and question identifiers.
- Source provenance values are preserved in API responses and export artifacts.

Test notes:
- Add parser-to-persistence integration tests asserting source metadata round-trips unchanged.
- Add API tests verifying provenance fields are present for each answer row.

### Story 4: Add actual-answer persistence and normalization pipeline
As a maintainer, I want actual-answer handling standardized so scoring can be deterministic.

Acceptance criteria:
- Actual answers are normalized through shared normalization rules before scoring.
- Null/blank/token variants are handled by explicit normalization policy, not silent defaults.
- Normalization diagnostics are captured for malformed or unknown answer tokens.

Test notes:
- Add normalization matrix tests for case, whitespace, aliases, and null-equivalent values.
- Add failure-path tests validating diagnostics for unsupported answer shapes.

### Story 5: Implement generic question scoring engine with pluggable strategies
As a product owner, I want category-based scoring strategies so preseason and H2H can share framework-level orchestration.

Acceptance criteria:
- Scoring pipeline resolves strategy by question category using a pluggable interface.
- Engine persists imported points, calculated points, delta, and reason code for each participant-question row.
- Engine behavior is deterministic for repeated runs of identical input.

Test notes:
- Add unit tests for strategy dispatch and fallback behavior when category strategy is missing.
- Add deterministic replay tests proving identical inputs produce identical outputs.

### Story 6: Add preseason migration adapter on top of generic framework
As an operator, I want preseason rows mapped through the generic framework so migration does not require a one-off preseason-only path.

Acceptance criteria:
- Preseason source rows map to generic template, answer, actual, and score entities.
- Existing preseason reconciliation semantics are preserved (imported vs calculated with reasoned deltas).
- Adapter emits validation errors with source row references for malformed preseason rows.

Test notes:
- Add fixture-driven adapter tests for valid preseason rows and malformed rows.
- Add regression tests to ensure preseason values remain isolated from race scoring entities.

### Story 7: Add H2H migration adapter on top of generic framework
As an operator, I want H2H rows mapped through the same framework so logic parity and supportability improve.

Acceptance criteria:
- H2H source rows map to the same generic entities used for preseason.
- H2H scoring strategy plugs into the shared engine without branching pipeline orchestration.
- Adapter supports unresolved token reporting and policy-driven handling.

Test notes:
- Add H2H fixture tests for parse, normalization, and scoring paths.
- Add unresolved-token tests validating warning/error policy behavior.

### Story 8: Add admin/API question reconciliation views
As an admin, I want filtered question diffs and summaries so I can review imported versus calculated values across categories.

Acceptance criteria:
- API exposes question-level diffs and aggregate summaries with filters for category, participant, expected-status, and non-zero delta.
- Admin UI supports the same filters with stable ordering and pagination.
- Exports include category, question id, imported points, calculated points, delta, and reason fields.

Test notes:
- Add API contract tests for filter combinations and deterministic ordering.
- Add web tests for filter state persistence and export payload shape checks.

### Story 9: Add extension test harness for future question category onboarding
As a developer, I want a test harness proving new categories can be added with minimal changes.

Acceptance criteria:
- Harness includes a reference mock category implemented end-to-end (template, parser mapping, strategy, reconciliation output).
- Adding a new category requires no core schema migration and no framework orchestration changes.
- Contributor guide documents steps for onboarding a new question category.

Test notes:
- Add a guard test that fails if core orchestrator requires category-specific branching.
- Add documentation lint/check verifying onboarding instructions are present and current.

## Delivery Plan
1. Canonical question schema and contracts
2. Ingestion and normalization pipeline
3. Scoring strategy abstraction
4. Preseason and H2H adapters
5. API and admin review surfaces
6. Extension and regression test coverage

## Risks and Mitigations
- Risk: Over-generalization blocks delivery.
- Mitigation: Start with preseason and H2H only while validating extension seams.

- Risk: Source format drift by competition.
- Mitigation: Keep adapter boundaries strict and mapped to shared domain contracts.

## Definition of Done
- Preseason and H2H question flows are persisted and reconciled through the same domain model.
- Imported and calculated question scores are both preserved with reasoned deltas.
- Admin/API can query question diffs with deterministic filtering and ordering.
- Tests demonstrate extension to a new mock question category without schema redesign.
