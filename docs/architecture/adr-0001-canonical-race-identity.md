# ADR-0001: Canonical Race Identity Contract

## Status
Accepted

## Date
2026-07-05

## Context
Race identity has drifted across services, tests, and route conventions, including legacy IDs such as `2026-01-albert_park` and newer competition-scoped IDs. This has created ambiguity in race targeting and made E2E verification non-deterministic.

We need one contract that is shared by:
- DataSyncWorker race generation
- API request validation and responses
- Web route resolution
- Integration and E2E tests

## Decision
Use a canonical race identifier composed as:

`competition-slug-season-round-race-slug`

Rules:
- `competition-slug` uses lowercase slug format (`[a-z0-9-]+`)
- `season` is a 4-digit year (`\d{4}`)
- `round` is a positive integer and is serialized as-is (no zero padding)
- `race-slug` uses lowercase slug format (`[a-z0-9-]+`)
- `competition-slug` does not duplicate season (for example `main`).

Example canonical RaceId:
- `main-2026-1-australian-grand-prix`

Hard-cutover policy:
- API/runtime behavior is canonical-only.
- No backward-compatibility alias table/layer is introduced in this phase.

## Routing Contract
API route forms that are supported:

1. Direct canonical race token routes
- `/selections/{raceId}/config`
- `/selections/{raceId}/mine`
- `/selections/{raceId}/current`
- `/races/{raceId}/metadata`

2. Context resolution routes (preferred for defaults/navigation)
- `/races/context/{competitionSlug}/{season}/round/{round}`
- `/races/context/{competitionSlug}/{season}/slug/{raceSlug}`

Context example for `main` and season `2026`:
- Round lookup: `/races/context/main/2026/round/1`
- Slug lookup: `/races/context/main/2026/slug/australian-grand-prix`

Expected context resolution payload example:

```json
{
  "raceId": "main-2026-1-australian-grand-prix",
  "competitionSlug": "main",
  "season": 2026,
  "round": 1,
  "raceSlug": "australian-grand-prix"
}
```

## Consequences
- Tests and fixtures must remove legacy race ID literals.
- API examples and OpenAPI docs should display canonical RaceId values.
- Web defaults should prefer context routes over hardcoded race IDs.