#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ENV_FILE="${1:-$ROOT_DIR/.env}"

if [[ ! -f "$ENV_FILE" ]]; then
    echo "ERROR: env file not found: $ENV_FILE" >&2
    exit 1
fi

read_env() {
    local key="$1"
    local line k v
    while IFS= read -r line || [[ -n "$line" ]]; do
        line="${line%$'\r'}"

        line="${line#"${line%%[![:space:]]*}"}"
        line="${line%"${line##*[![:space:]]}"}"

        [[ -z "$line" || "${line:0:1}" == "#" ]] && continue
        [[ "$line" != *=* ]] && continue

        k="${line%%=*}"
        v="${line#*=}"

        k="${k%"${k##*[![:space:]]}"}"
        k="${k#"${k%%[![:space:]]*}"}"

        [[ "$k" != "$key" ]] && continue

        v="${v%"${v##*[![:space:]]}"}"
        v="${v#"${v%%[![:space:]]*}"}"

        if [[ ${#v} -ge 2 ]]; then
            if [[ ( "${v:0:1}" == '"' && "${v: -1}" == '"' ) || ( "${v:0:1}" == "'" && "${v: -1}" == "'" ) ]]; then
                v="${v:1:${#v}-2}"
            fi
        fi

        printf '%s\n' "$v"
        return 0
    done < "$ENV_FILE"

    return 0
}

HOST_PORT="$(read_env HOST_PORT)"
HOST_PORT_WEB="$(read_env HOST_PORT_WEB)"
HOST_PORT="${HOST_PORT:-5000}"
HOST_PORT_WEB="${HOST_PORT_WEB:-5001}"

echo "Checking compose service state..."
docker compose --env-file "$ENV_FILE" ps

echo "Checking API health endpoint on port $HOST_PORT..."
curl --fail --silent --show-error "http://127.0.0.1:${HOST_PORT}/health" >/dev/null

echo "Checking web root on port $HOST_PORT_WEB..."
curl --fail --silent --show-error --head "http://127.0.0.1:${HOST_PORT_WEB}/" >/dev/null

echo "Checking worker state..."
worker_status="$(docker compose --env-file "$ENV_FILE" ps --status running --services 2>/dev/null | grep -x 'f1-data-sync-worker' || true)"
if [[ -n "$worker_status" ]]; then
    echo "- worker is running"
else
    worker_container_id="$(docker compose --env-file "$ENV_FILE" ps -q f1-data-sync-worker)"
    if [[ -n "$worker_container_id" ]]; then
        worker_exit_code="$(docker inspect --format '{{.State.ExitCode}}' "$worker_container_id")"
        if [[ "$worker_exit_code" == "0" ]]; then
            echo "- worker exited cleanly (one-shot mode or completed run)"
        else
            echo "ERROR: worker is not running and last exit code was $worker_exit_code" >&2
            exit 1
        fi
    else
        echo "ERROR: worker container not found in compose project" >&2
        exit 1
    fi
fi

echo "Smoke checks passed."
