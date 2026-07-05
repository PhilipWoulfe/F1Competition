# Epic: Race ID Alignment and E2E Selection Reliability

## Summary
Unify race identity across seeded data, API, web routes, and E2E tests so race selection flows always target the same race and produce deterministic assertions.

This epic addresses drift between:
- Legacy race ID assumptions (for example 2026-01-albert_park)
- Current DataSyncWorker-generated IDs (competition-slug based)
- E2E route resolution and API verification behavior

Decision update:
- We will do a hard cutover to canonical race IDs.
- No backward-compatibility alias layer will be implemented in this phase.
- Existing legacy literals in tests/config are updated or removed.

## Problem Statement
Current behavior allows multiple race ID conventions to coexist in tests and code paths. This causes:
- Selection requests targeting the wrong race record
- Test flakiness due to pre-existing selections satisfying weak assertions
- Confusion about canonical race identity and backward compatibility

## Goals
- Define one canonical race identity contract for runtime behavior.
- Ensure E2E tests verify true end-to-end correctness (not partial signals).
- Keep seeded data and defaults aligned with runtime contract.

## Scope
In scope:
- DB schema and migration changes needed for canonical race identity only
- API changes for canonical race ID resolution and consistent endpoint behavior
- Web changes to route and fetch against canonical race identity
- DataSyncWorker race ID generation and seed consistency updates
- Seed/config default alignment (.env.example, docs, test defaults)
- E2E and integration test hardening for selection validation

Out of scope:
- Scoring rules redesign
- Historical results backfill beyond race identity mapping requirements
- Non-selection UX redesign

## Canonical Contract (Target State)
- Canonical race key: competition-slug + season + round + race-slug.
- API only accepts canonical race IDs and context routes that resolve to canonical IDs.
- Web route defaults favor context routes (competition/season/round or competition/season/race-slug), not hardcoded legacy IDs.

## User Stories

### Story 1: Define and publish race identity contract
As an engineer, I want an explicit race identity contract so all services and tests target race records consistently.

Acceptance criteria:
- Architecture decision note added under docs describing canonical ID format and routing usage.
- Contract examples include main-2026 context and canonical RaceId examples.
- API/OpenAPI examples updated to reflect canonical response RaceId values.

### Story 2: Remove legacy race ID usage from DB and test fixtures
As a platform maintainer, I want canonical race IDs to be the only seeded/tested IDs so behavior is unambiguous.

Acceptance criteria:
- Any legacy ID literals used as defaults/fixtures are replaced with canonical IDs or context resolution.
- No alias table is introduced.
- Migrations remain idempotent and leave only canonical IDs in seeded records.

### Story 3: Normalize race ID input in API endpoints
As an API consumer, I want race-selection endpoints to operate only on canonical race IDs.

Acceptance criteria:
- Selections, metadata, and race-config endpoints validate canonical race token usage before read/write.
- 404 behavior is preserved for unknown tokens.
- Endpoint payloads include canonical RaceId.
- API tests cover canonical input and unknown input.

### Story 4: Align web routing and race resolution
As a user, I want the selection page to always resolve to the intended race regardless of URL form.

Acceptance criteria:
- Selection routes continue to support direct race token and context routes.
- Error messaging remains clear for unresolved race context.
- Web unit tests cover canonical route scenarios and invalid token handling.

### Story 5: Update DataSyncWorker race ID generation
As a data engineer, I want DataSyncWorker to generate canonical IDs consistently.

Acceptance criteria:
- Race ID generation follows documented canonical contract.
- Worker remains idempotent across repeated runs.
- Existing relational idempotency tests confirm canonical ID stability.

### Story 6: Align seeded/test data and local defaults
As a developer, I want local config and seeded data examples to match runtime race identity behavior.

Acceptance criteria:
- .env.example E2E guidance uses context-first defaults and documents optional explicit race ID override behavior.
- Seed fixtures in tests avoid legacy literals.
- README and test docs describe canonical-only behavior.

### Story 7: Harden E2E selection assertions
As a QA engineer, I want E2E selection tests to verify full submitted state so false positives are eliminated.

Acceptance criteria:
- E2E persistence check validates full ordered selection set and expected count, not a single driver match.
- E2E tests assert resolved race ID and route race context match before submit.
- Trace logs capture canonical race ID and submission payload summary for debugging.
- Flaky-path retries are limited to transient transport failures only.

## Delivery Plan
1. Contract and canonical-only decision note
2. DB/test fixture cleanup to canonical IDs
3. API canonical validation and tests
4. Web route normalization and tests
5. DataSyncWorker canonical stability tests
6. E2E hardening and docs/env updates

## Risks and Mitigations
- Risk: Existing tests/tools still referencing legacy race IDs will fail after cutover.
- Mitigation: replace all known literals in a single coordinated change and add grep-based CI guard.

- Risk: Data drift if worker and API use different normalization logic.
- Mitigation: shared normalization utility and cross-layer contract tests.

## Definition of Done
- All stories above accepted.
- DB migration/seed updates proven idempotent in automated tests.
- API/Web/E2E tests green with deterministic race targeting.
- Documentation updated for canonical race identity behavior.

## Anything Missing? (Recommended Additions)
- Temporary CI check that fails on newly introduced legacy race ID patterns.
- One-time repository cleanup checklist for old literals in docs/tests/scripts.
- Contract test suite that runs against API and web route resolution in CI.
