#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'HELP'
Usage:
  ./scripts/github-sync-production-env.sh --repo owner/repo --vars-file path --secrets-file path [--env production] [--dry-run]

Examples:
  ./scripts/github-sync-production-env.sh \
    --repo PhilipWoulfe/F1Competition \
    --vars-file .github/env/production.vars.env \
    --secrets-file .github/env/production.secrets.env

  ./scripts/github-sync-production-env.sh \
    --repo PhilipWoulfe/F1Competition \
    --vars-file .github/env/production.vars.env \
    --secrets-file .github/env/production.secrets.env \
    --env production \
    --dry-run

Notes:
  - Requires GitHub CLI (`gh`) authenticated with repo admin permissions.
  - Vars file lines are applied with: gh variable set --env <env>
  - Secrets file lines are applied with: gh secret set --env <env>
  - Files must be dotenv style: KEY=value
  - Surrounding quotes are stripped; whitespace around '=' is allowed.
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

parse_env_file() {
    local file_path="$1"
    while IFS= read -r line || [[ -n "$line" ]]; do
        line="${line%$'\r'}"
        line="$(trim "$line")"
        [[ -z "$line" || "${line:0:1}" == "#" ]] && continue
        [[ "$line" != *=* ]] && continue

        local key="${line%%=*}"
        local value="${line#*=}"

        key="$(trim "$key")"
        value="$(trim "$value")"
        value="$(strip_quotes "$value")"

        [[ -z "$key" ]] && continue
        printf '%s\t%s\n' "$key" "$value"
    done < "$file_path"
}

ensure_tooling() {
    if ! command -v gh >/dev/null 2>&1; then
        echo "ERROR: GitHub CLI 'gh' is not installed." >&2
        exit 1
    fi

    if ! gh auth status >/dev/null 2>&1; then
        echo "ERROR: 'gh' is not authenticated. Run: gh auth login" >&2
        exit 1
    fi
}

REPO=""
ENV_NAME="production"
VARS_FILE=""
SECRETS_FILE=""
DRY_RUN=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --repo)
            REPO="${2:-}"
            shift 2
            ;;
        --env)
            ENV_NAME="${2:-}"
            shift 2
            ;;
        --vars-file)
            VARS_FILE="${2:-}"
            shift 2
            ;;
        --secrets-file)
            SECRETS_FILE="${2:-}"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            echo "ERROR: Unknown argument: $1" >&2
            usage
            exit 1
            ;;
    esac
done

if [[ -z "$REPO" || -z "$VARS_FILE" || -z "$SECRETS_FILE" ]]; then
    echo "ERROR: --repo, --vars-file, and --secrets-file are required." >&2
    usage
    exit 1
fi

if [[ ! -f "$VARS_FILE" ]]; then
    echo "ERROR: vars file not found: $VARS_FILE" >&2
    exit 1
fi

if [[ ! -f "$SECRETS_FILE" ]]; then
    echo "ERROR: secrets file not found: $SECRETS_FILE" >&2
    exit 1
fi

ensure_tooling

echo "Applying GitHub Environment variables for '$ENV_NAME' in '$REPO'..."
while IFS=$'\t' read -r key value; do
    if [[ -z "$key" ]]; then
        continue
    fi

    if [[ "$DRY_RUN" == true ]]; then
        echo "[dry-run] gh variable set $key --env $ENV_NAME --repo $REPO --body <redacted>"
    else
        gh variable set "$key" --env "$ENV_NAME" --repo "$REPO" --body "$value"
        echo "Set variable: $key"
    fi
done < <(parse_env_file "$VARS_FILE")

echo "Applying GitHub Environment secrets for '$ENV_NAME' in '$REPO'..."
while IFS=$'\t' read -r key value; do
    if [[ -z "$key" ]]; then
        continue
    fi

    if [[ "$DRY_RUN" == true ]]; then
        echo "[dry-run] gh secret set $key --env $ENV_NAME --repo $REPO --body <redacted>"
    else
        gh secret set "$key" --env "$ENV_NAME" --repo "$REPO" --body "$value"
        echo "Set secret: $key"
    fi
done < <(parse_env_file "$SECRETS_FILE")

echo "Done."
