# OpenAPI Examples: Canonical Race Identity

This document defines example request/response payloads to align API/OpenAPI examples with canonical race identity.

## Canonical RaceId

Format:

`competitionSlug-round-raceSlug`

Example:

`main-2026-1-australian-grand-prix`

## Context Resolution Examples

### Resolve by round

`GET /races/context/main-2026/round/1`

```json
{
  "raceId": "main-2026-1-australian-grand-prix",
  "competitionSlug": "main-2026",
  "round": 1,
  "raceSlug": "australian-grand-prix"
}
```

### Resolve by slug

`GET /races/context/main-2026/slug/australian-grand-prix`

```json
{
  "raceId": "main-2026-1-australian-grand-prix",
  "competitionSlug": "main-2026",
  "round": 1,
  "raceSlug": "australian-grand-prix"
}
```

## Selection Config Response Example

`GET /selections/main-2026-1-australian-grand-prix/config`

```json
{
  "raceId": "main-2026-1-australian-grand-prix",
  "selectionCount": 5,
  "preQualyDeadlineUtc": "2026-03-14T04:00:00Z",
  "finalDeadlineUtc": "2026-03-15T03:00:00Z",
  "earlyLockBetType": "PreQualy",
  "earlyLockLabel": "Pre-Qualy lock",
  "finalSubmissionLabel": "Final submission",
  "lockMessage": "Locking for Pre-Qualy gives +50% points and prevents changes after the configured lock deadline.",
  "lockedSelectionMessage": "This pre-qualy selection is locked.",
  "betOptions": [
    {
      "betType": "PreQualy",
      "label": "Pre-Qualy",
      "description": "Submit before pre-qualifying starts for bonus points.",
      "isAvailable": true
    },
    {
      "betType": "RaceStart",
      "label": "Race Start",
      "description": "Submit before race start.",
      "isAvailable": true
    }
  ]
}
```

## Metadata Response Example

`GET /races/main-2026-1-australian-grand-prix/metadata`

```json
{
  "raceId": "main-2026-1-australian-grand-prix",
  "h2hQuestion": "Who will qualify higher: Piastri or Norris?",
  "bonusQuestion": "Will there be a safety car?",
  "isPublished": true,
  "updatedAtUtc": "2026-03-10T09:30:00Z",
  "eTag": "W/\"v12\""
}
```