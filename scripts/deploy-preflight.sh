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

require_non_empty() {
    local key="$1"
    local value
    value="$(read_env "$key")"
    if [[ -z "$value" ]]; then
        echo "ERROR: required key '$key' is missing or empty in $ENV_FILE" >&2
        exit 1
    fi
}

HOST_LOG_PATH="$(read_env HOST_LOG_PATH)"
MIN_FREE_DISK_MB="$(read_env DEPLOY_MIN_FREE_DISK_MB)"
COMPOSE_PROFILES="$(read_env COMPOSE_PROFILES)"

HOST_LOG_PATH="${HOST_LOG_PATH:-/tmp/f1api-logs}"
MIN_FREE_DISK_MB="${MIN_FREE_DISK_MB:-1024}"

require_non_empty POSTGRES_DB
require_non_empty POSTGRES_USER
require_non_empty POSTGRES_PASSWORD
require_non_empty TAG
require_non_empty CLOUDFLARE_AUDIENCE
require_non_empty CLOUDFLARE_ISSUER

if [[ ",${COMPOSE_PROFILES}," == *",cloud,"* ]]; then
    require_non_empty TUNNEL_TOKEN
fi

mkdir -p "$HOST_LOG_PATH"

if [[ ! -w "$HOST_LOG_PATH" ]]; then
    echo "ERROR: HOST_LOG_PATH is not writable: $HOST_LOG_PATH" >&2
    exit 1
fi

probe_file="$HOST_LOG_PATH/.write-test-$$"
trap 'rm -f "$probe_file"' EXIT
printf 'ok' > "$probe_file"
rm -f "$probe_file"

available_kb="$(df -Pk "$HOST_LOG_PATH" | awk 'NR==2 { print $4 }')"
available_mb=$((available_kb / 1024))

if (( available_mb < MIN_FREE_DISK_MB )); then
    echo "ERROR: only ${available_mb}MB free at $HOST_LOG_PATH; require at least ${MIN_FREE_DISK_MB}MB" >&2
    exit 1
fi

docker compose --env-file "$ENV_FILE" config >/dev/null

echo "Preflight checks passed."
echo "- env file: $ENV_FILE"
echo "- writable log path: $HOST_LOG_PATH"
echo "- free disk: ${available_mb}MB"
