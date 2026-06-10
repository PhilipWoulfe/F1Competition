#!/usr/bin/env bash
set -euo pipefail

usage() {
    cat <<'HELP'
Usage:
  sudo ./scripts/bootstrap-vm.sh [options]

Options:
  --deploy-user <name>         Deploy user to create/configure (default: deploy)
  --deploy-path <path>         Runtime path for compose files (default: /opt/f1competition)
  --log-path <path>            Host log path for API logs (default: /mnt/f1-logs)
  --tailscale-auth-key <key>   Optional Tailscale auth key for non-interactive join
  --skip-tailscale-up          Install Tailscale but do not run tailscale up
  -h, --help                   Show help

Notes:
  - Intended for Ubuntu 24.04+.
  - Must run as root.
HELP
}

ensure_root() {
    if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
        echo "ERROR: run as root (sudo)." >&2
        exit 1
    fi
}

DEPLOY_USER="deploy"
DEPLOY_PATH="/opt/f1competition"
LOG_PATH="/mnt/f1-logs"
TAILSCALE_AUTH_KEY=""
SKIP_TAILSCALE_UP=false

while [[ $# -gt 0 ]]; do
    case "$1" in
        --deploy-user)
            DEPLOY_USER="${2:-}"
            shift 2
            ;;
        --deploy-path)
            DEPLOY_PATH="${2:-}"
            shift 2
            ;;
        --log-path)
            LOG_PATH="${2:-}"
            shift 2
            ;;
        --tailscale-auth-key)
            TAILSCALE_AUTH_KEY="${2:-}"
            shift 2
            ;;
        --skip-tailscale-up)
            SKIP_TAILSCALE_UP=true
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

ensure_root

echo "Installing OS dependencies..."
apt-get update
apt-get install -y ca-certificates curl gnupg git ufw unattended-upgrades

echo "Configuring Docker apt repository..."
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg
chmod a+r /etc/apt/keyrings/docker.gpg

source /etc/os-release
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu ${VERSION_CODENAME} stable" \
    | tee /etc/apt/sources.list.d/docker.list > /dev/null

echo "Installing Docker Engine and Compose plugin..."
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
systemctl enable docker
systemctl start docker

echo "Installing Tailscale..."
curl -fsSL https://tailscale.com/install.sh | sh

if [[ "$SKIP_TAILSCALE_UP" == false ]]; then
    if [[ -n "$TAILSCALE_AUTH_KEY" ]]; then
        tailscale up --authkey "$TAILSCALE_AUTH_KEY"
    else
        echo "Running interactive 'tailscale up' (no auth key provided)."
        tailscale up
    fi
fi

if ! id "$DEPLOY_USER" >/dev/null 2>&1; then
    echo "Creating deploy user '$DEPLOY_USER'..."
    useradd -m -s /bin/bash "$DEPLOY_USER"
fi

echo "Configuring deploy user and runtime directories..."
usermod -aG docker "$DEPLOY_USER"
mkdir -p "$DEPLOY_PATH/scripts" "$LOG_PATH"
chown -R "$DEPLOY_USER:$DEPLOY_USER" "$DEPLOY_PATH" "$LOG_PATH"

echo "Enabling unattended security upgrades..."
dpkg-reconfigure -f noninteractive unattended-upgrades || true

echo "Applying baseline firewall rules..."
ufw --force default deny incoming
ufw --force default allow outgoing
ufw allow in on tailscale0 || true
ufw --force enable

cat <<EOF
Bootstrap complete.

Summary:
- Deploy user: $DEPLOY_USER
- Deploy path: $DEPLOY_PATH
- Log path: $LOG_PATH

Next steps:
1. Copy docker-compose.yml and scripts into $DEPLOY_PATH
2. Create $DEPLOY_PATH/.env
3. Run deploy preflight and smoke checks
EOF
