#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'HELP'
Usage:
  ./scripts/generate-github-env-files.sh [options]

Options:
  --source-env <path>     Source .env to read (default: .env)
  --out-dir <path>        Output directory (default: .github/env)
  --test-tag <tag>        TAG value for test output (default: test)
  --prod-tag <tag>        TAG value for production output (default: stable)
  --overwrite             Overwrite existing output files
  -h, --help              Show help

Outputs:
  <out-dir>/test.vars.env
  <out-dir>/test.secrets.env
  <out-dir>/production.vars.env
  <out-dir>/production.secrets.env

Notes:
  - This script reads your current runtime .env (for example on LXC) and splits values
    into GitHub Environment variables vs secrets for both test and production.
  - Review generated files before importing to GitHub.
HELP
}

trim() {
    local value="$1"
    value="${value#"${value%%[![:space:]]*}"}"
    value="${value%"${value##*[![:space:]]}"}"
    printf '%s' "$value"
}

strip_quotes() {
    local value="$1"
    if [[ ${#value} -ge 2 ]]; then
        if [[ ( "${value:0:1}" == '"' && "${value: -1}" == '"' ) || ( "${value:0:1}" == "'" && "${value: -1}" == "'" ) ]]; then
            value="${value:1:${#value}-2}"
        fi
    fi
    printf '%s' "$value"
}

get_env_value() {
    local env_file="$1"
    local key="$2"
    local line k v
    while IFS= read -r line || [[ -n "$line" ]]; do
        line="${line%$'\r'}"
        line="$(trim "$line")"
        [[ -z "$line" || "${line:0:1}" == "#" ]] && continue
        [[ "$line" != *=* ]] && continue

        k="${line%%=*}"
        v="${line#*=}"
        k="$(trim "$k")"
        [[ "$k" != "$key" ]] && continue
        v="$(trim "$v")"
        v="$(strip_quotes "$v")"
        printf '%s' "$v"
        return 0
    done < "$env_file"

    return 0
}

write_kv_file() {
    local output_file="$1"
    shift
    local keys=("$@")

    : > "$output_file"
    for key in "${keys[@]}"; do
        local value
        value="$(get_env_value "$SOURCE_ENV" "$key")"
        printf '%s=%s\n' "$key" "$value" >> "$output_file"
    done
}

SOURCE_ENV=".env"
OUT_DIR=".github/env"
TEST_TAG="test"
PROD_TAG="stable"
OVERWRITE=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --source-env)
            SOURCE_ENV="${2:-}"
            shift 2
            ;;
        --out-dir)
            OUT_DIR="${2:-}"
            shift 2
            ;;
        --test-tag)
            TEST_TAG="${2:-}"
            shift 2
            ;;
        --prod-tag)
            PROD_TAG="${2:-}"
            shift 2
            ;;
        --overwrite)
            OVERWRITE=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "ERROR: unknown argument: $1" >&2
            usage
            exit 1
            ;;
    esac
done

if [[ ! -f "$SOURCE_ENV" ]]; then
    echo "ERROR: source env file not found: $SOURCE_ENV" >&2
    exit 1
fi

mkdir -p "$OUT_DIR"

test_vars_file="$OUT_DIR/test.vars.env"
test_secrets_file="$OUT_DIR/test.secrets.env"
prod_vars_file="$OUT_DIR/production.vars.env"
prod_secrets_file="$OUT_DIR/production.secrets.env"

if [[ "$OVERWRITE" != true ]]; then
    for file in "$test_vars_file" "$test_secrets_file" "$prod_vars_file" "$prod_secrets_file"; do
        if [[ -f "$file" ]]; then
            echo "ERROR: output file already exists: $file (use --overwrite)" >&2
            exit 1
        fi
    done
fi

VAR_KEYS=(
    VM_TAILSCALE_HOST
    VM_DEPLOY_USER
    VM_DEPLOY_PATH
    GHCR_USERNAME
    E2E_BASE_URL
    E2E_API_BASE_URL
    E2E_RACE_ID
)

SECRET_KEYS=(
    TAILSCALE_OAUTH_CLIENT_ID
    TAILSCALE_OAUTH_SECRET
    VM_SSH_PRIVATE_KEY
    GHCR_READ_TOKEN
    E2E_CF_CLIENT_ID
    E2E_CF_CLIENT_SECRET
)

ENV_KEYS=(
    TAG
    CONTAINER_NAME
    HOST_PORT
    HOST_PORT_WEB
    ALLOWED_ORIGINS
    BLAZOR_ENVIRONMENT
    API_BASE_URL
    POSTGRES_DB
    POSTGRES_USER
    POSTGRES_PASSWORD
    POSTGRES_PORT
    DB_AUTO_MIGRATE
    DATA_SYNC_INTERVAL_MINUTES
    DATA_SYNC_AUTO_MIGRATE
    DATA_SYNC_HTTP_RETRY_COUNT
    DATA_SYNC_HTTP_RETRY_DELAY_MS
    DATA_SYNC_DEADLINE_MINUTES_BEFORE_START
    DATA_SYNC_JOLPICA_BASE_URL
    DATA_SYNC_CONTINUE_ON_ERROR
    CLOUDFLARE_AUDIENCE
    CLOUDFLARE_ISSUER
    ADMIN_GROUP_CLAIM_TYPE
    ADMIN_GROUPS
    ADMIN_EMAILS
    CLOUDFLARE_ENABLE_TEST_SERVICE_TOKEN_FALLBACK
    CLOUDFLARE_TEST_SERVICE_TOKEN_SUBJECT_ALLOWLIST
    CLOUDFLARE_TEST_SERVICE_TOKEN_ADMIN_SUBJECT_ALLOWLIST
    CLOUDFLARE_TEST_SERVICE_TOKEN_EMAIL_DOMAIN
    HOST_LOG_PATH
    DEPLOY_MIN_FREE_DISK_MB
    DEV_MOCK_EMAIL
    DEV_MOCK_GROUPS
    DEV_ENABLE_DEBUG_ENDPOINTS
    COMPOSE_PROFILES
    TUNNEL_TOKEN
)

write_kv_file "$test_vars_file" "${VAR_KEYS[@]}"
write_kv_file "$test_secrets_file" "${SECRET_KEYS[@]}"
write_kv_file "$prod_vars_file" "${VAR_KEYS[@]}"
write_kv_file "$prod_secrets_file" "${SECRET_KEYS[@]}"

# Generate environment payloads from current .env and override TAG per environment.
test_env_payload=""
for key in "${ENV_KEYS[@]}"; do
    value="$(get_env_value "$SOURCE_ENV" "$key")"
    if [[ "$key" == "TAG" ]]; then
        value="$TEST_TAG"
    fi
    test_env_payload+="${key}=${value}"$'\n'
done

prod_env_payload=""
for key in "${ENV_KEYS[@]}"; do
    value="$(get_env_value "$SOURCE_ENV" "$key")"
    if [[ "$key" == "TAG" ]]; then
        value="$PROD_TAG"
    fi
    prod_env_payload+="${key}=${value}"$'\n'
done

printf 'VM_ENV_FILE="%s"\n' "${test_env_payload//$'\n'/\\n}" >> "$test_secrets_file"
printf 'VM_ENV_FILE="%s"\n' "${prod_env_payload//$'\n'/\\n}" >> "$prod_secrets_file"

echo "Generated files:"
echo "- $test_vars_file"
echo "- $test_secrets_file"
echo "- $prod_vars_file"
echo "- $prod_secrets_file"

echo
echo "Next steps:"
echo "1. Review generated files and fill any blank values."
echo "2. Sync test environment:"
echo "   ./scripts/github-sync-environment.sh --repo <owner/repo> --vars-file $test_vars_file --secrets-file $test_secrets_file --env test"
echo "3. Sync production environment:"
echo "   ./scripts/github-sync-environment.sh --repo <owner/repo> --vars-file $prod_vars_file --secrets-file $prod_secrets_file --env production"
