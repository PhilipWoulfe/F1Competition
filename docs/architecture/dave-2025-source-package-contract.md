# Dave 2025 Source Package Contract

## Purpose
Define the migration input contract for the Dave 2025 multi-file package.

## Required Files
- races.csv
- bonus.csv
- bonusAnswers.csv
- Leaderboard.csv

## Optional Files
- raceResults.ps1
- MostOF the boionus Questions.txt

## Schema Expectations

### races.csv
- Must include a header with Name and RaceN-* columns.
- Must include one _Result row for race outcomes.
- Participant rows are all non-_Result rows with non-empty Name.

### bonus.csv
- Must include Question as first column.
- Must include participant columns matching competition participants.

### bonusAnswers.csv
- Must include Question and Answer columns.
- Notes column is optional metadata.

### Leaderboard.csv
- Must include Name plus summary columns used for reconciliation.
- Expected summary fields include Points, Bets, Total, Bonus, and CDP where present.

## Validation Behavior
- If package detection rules identify Dave 2025 package semantics, all required files must exist.
- Missing required files cause startup failure before staging.
- Extra files are diagnostic-only and do not block execution.

## Checksum Behavior
- Package checksum is a deterministic manifest hash:
  - For each top-level file, compute file SHA-256.
  - Sort by file name case-insensitively.
  - Hash manifest lines in form fileName|fileHash.
- The resulting manifest hash is persisted as SourceFileChecksum for run-level idempotency/parity.