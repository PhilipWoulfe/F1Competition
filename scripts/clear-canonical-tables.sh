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
SQL
)

if [[ "$include_competitions" == 'true' ]]; then
  sql+=$'\nDELETE FROM public."Competitions";'
fi

sql+=$'\nCOMMIT;\n'

sql_file=$(mktemp)
cleanup() {
  rm -f "$sql_file"
}
trap cleanup EXIT

printf '%s' "$sql" > "$sql_file"

if command -v psql >/dev/null 2>&1; then
  if [[ -n "$conn" ]]; then
    psql "$conn" -v ON_ERROR_STOP=1 -f "$sql_file"
  else
    psql -v ON_ERROR_STOP=1 -f "$sql_file"
  fi
else
  if [[ -n "$conn" ]]; then
    DATABASE_URL="$conn" docker compose exec -T postgres sh -lc 'psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f /dev/stdin' < "$sql_file"
  else
    PGPASSWORD="${POSTGRES_PASSWORD:-f1}" docker compose exec -T postgres psql -U "${POSTGRES_USER:-f1}" -d "${POSTGRES_DB:-f1competition}" -v ON_ERROR_STOP=1 -f /dev/stdin < "$sql_file"
  fi
fi

echo 'Canonical table clear-down complete.'
