#!/usr/bin/env bash
# Installs the Argus agent as a systemd service.
#
#   curl -fsSL https://argus.example.com/downloads/install.sh | sudo bash -s -- \
#       --server https://argus.example.com --token argus_et_...
#
# Options:
#   --server URL    The Argus server the agent reports to (required).
#   --token TOKEN   Enrollment token from the web UI. Required for a first install;
#                   reinstalling an already registered agent keeps its identity.
#   --binary PATH   Install this agent binary instead of downloading it from the server.
set -euo pipefail

SERVER=""
TOKEN=""
BINARY=""
INSTALL_DIR=/opt/argus-agent
CONFIG_DIR=/etc/argus-agent
STATE_DIR=/var/lib/argus-agent
SERVICE=argus-agent
SERVICE_USER=argus-agent

fail() {
  echo "argus-agent install: $*" >&2
  exit 1
}

step() {
  echo "==> $*"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --server) SERVER="${2:-}"; shift 2 ;;
    --token) TOKEN="${2:-}"; shift 2 ;;
    --binary) BINARY="${2:-}"; shift 2 ;;
    *) fail "unknown option '$1' (expected --server, --token or --binary)" ;;
  esac
done

[ "$(id -u)" -eq 0 ] || fail "run this as root, for example with sudo"
command -v systemctl >/dev/null 2>&1 || fail "this installer needs systemd"

SERVER="${SERVER%/}"
case "$SERVER" in
  http://* | https://*) ;;
  *) fail "--server must be the server's http(s) address, e.g. https://argus.example.com" ;;
esac
case "$SERVER" in
  *[[:space:]\"\\]*) fail "--server contains characters that cannot be part of a URL" ;;
esac
if [ -n "$TOKEN" ] && ! printf '%s' "$TOKEN" | grep -Eq '^argus_et_[A-Za-z0-9_-]+$'; then
  fail "--token does not look like an enrollment token (they start with argus_et_)"
fi

case "$(uname -m)" in
  x86_64 | amd64) RUNTIME=linux-x64 ;;
  aarch64 | arm64) RUNTIME=linux-arm64 ;;
  *) fail "unsupported processor architecture: $(uname -m)" ;;
esac

download() {
  if command -v curl >/dev/null 2>&1; then
    curl -fsSL "$1" -o "$2"
  elif command -v wget >/dev/null 2>&1; then
    wget -qO "$2" "$1"
  else
    fail "curl or wget is needed to download the agent"
  fi
}

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

if [ -z "$BINARY" ]; then
  step "Downloading the agent for $RUNTIME"
  download "$SERVER/downloads/agent/$RUNTIME/argus-agent" "$WORK/argus-agent" \
    || fail "could not download the agent from $SERVER"
  BINARY="$WORK/argus-agent"
fi
[ -f "$BINARY" ] || fail "agent binary not found: $BINARY"

# Use the unit file shipped next to this script (archive installs), else fetch it from the server.
UNIT="$(dirname -- "${BASH_SOURCE[0]:-$0}")/argus-agent.service"
if [ ! -f "$UNIT" ]; then
  download "$SERVER/downloads/argus-agent.service" "$WORK/argus-agent.service" \
    || fail "could not download the service definition from $SERVER"
  UNIT="$WORK/argus-agent.service"
fi

if ! id "$SERVICE_USER" >/dev/null 2>&1; then
  step "Creating the $SERVICE_USER system user"
  useradd --system --no-create-home --home-dir "$STATE_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

systemctl stop "$SERVICE" >/dev/null 2>&1 || true

step "Installing into $INSTALL_DIR"
install -d -m 0755 "$INSTALL_DIR"
install -m 0755 "$BINARY" "$INSTALL_DIR/argus-agent"
install -d -m 0750 -o root -g "$SERVICE_USER" "$CONFIG_DIR"
install -d -m 0700 -o "$SERVICE_USER" -g "$SERVICE_USER" "$STATE_DIR"

CONFIG="$CONFIG_DIR/agent.json"
if [ -n "$TOKEN" ] || [ ! -f "$CONFIG" ]; then
  [ -n "$TOKEN" ] || fail "--token is required for a first install (create one under 'Add a system' in the web UI)"
  step "Writing $CONFIG"
  (
    umask 027
    printf '{\n  "ServerUrl": "%s",\n  "EnrollmentToken": "%s"\n}\n' "$SERVER" "$TOKEN" > "$CONFIG"
  )
  chown root:"$SERVICE_USER" "$CONFIG"
  chmod 0640 "$CONFIG"
fi

install -m 0644 "$UNIT" "/etc/systemd/system/$SERVICE.service"
systemctl daemon-reload
systemctl enable --now "$SERVICE" >/dev/null

step "Done. The agent reports to $SERVER and shows up in the web UI within a minute."
echo "    Status: systemctl status $SERVICE"
echo "    Logs:   journalctl -u $SERVICE -f"
