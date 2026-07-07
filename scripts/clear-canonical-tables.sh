#!/usr/bin/env bash
set -euo pipefail

# Clears canonical domain tables used by the migration write path.
#
# Usage:
#   scripts/clear-canonical-tables.sh
#   scripts/clear-canonical-tables.sh --yes
#
# Connection resolution (first match wins):
#   1) DATABASE_URL
#   2) ConnectionStrings__Postgres
#   3) PGHOST/PGPORT/PGDATABASE/PGUSER/PGPASSWORD via psql defaults

force='false'
include_competitions='false'

while [[ $# -gt 0 ]]; do
  case "$1" in
    --yes|-y)
      force='true'
      ;;
    --include-competitions)
      include_competitions='true'
      ;;
    --help|-h)
      cat <<'USAGE'
Clear canonical tables in Postgres.

Options:
  --yes, -y                 Skip confirmation prompt
  --include-competitions    Also delete rows from public."Competitions"
  --help, -h                Show this help text
USAGE
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
  shift
done

if [[ "$force" != 'true' ]]; then
  cat <<'WARN'
This will permanently delete data from canonical tables:
  - public."QuestionScores"
  - public."QuestionAnswers"
  - public."QuestionActuals"
  - public."QuestionTemplates"
  - public."SelectionPositions"
  - public."Selections"
  - public."RaceMetadata"
  - public."Drivers"
WARN

  if [[ "$include_competitions" == 'true' ]]; then
    echo '  - public."Competitions"'
  fi

  read -r -p 'Type CLEAR to continue: ' confirm
  if [[ "$confirm" != 'CLEAR' ]]; then
    echo 'Aborted.'
    exit 1
  fi
fi

conn=''
if [[ -n "${DATABASE_URL:-}" ]]; then
  conn="$DATABASE_URL"
elif [[ -n "${ConnectionStrings__Postgres:-}" ]]; then
  conn="$ConnectionStrings__Postgres"
fi

sql=$(cat <<'SQL'
BEGIN;
DELETE FROM public."QuestionScores";
DELETE FROM public."QuestionAnswers";
DELETE FROM public."QuestionActuals";
DELETE FROM public."QuestionTemplates";
DELETE FROM public."SelectionPositions";
DELETE FROM public."Selections";
DELETE FROM public."RaceMetadata";
DELETE FROM public."Drivers";
SQL
)

if [[ "$include_competitions" == 'true' ]]; then
  sql+=$'\nDELETE FROM public."Competitions";'
fi

sql+=$'\nCOMMIT;\n'

if [[ -n "$conn" ]]; then
  psql "$conn" -v ON_ERROR_STOP=1 -c "$sql"
else
  psql -v ON_ERROR_STOP=1 -c "$sql"
fi

echo 'Canonical table clear-down complete.'
