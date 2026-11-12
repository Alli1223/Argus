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
#   --docker        Let the agent watch Docker's containers. It joins the docker group for that, which lets
#                   it use Docker as root could. Reinstalling without the option leaves this as it is.
#   --container-actions
#                   Also let people who can see this machine in Argus read its containers' logs and start,
#                   stop and restart them. Implies --docker. Reinstalling without it leaves this as it is.
#   --power-actions Also let people who can see this machine in Argus, and their alert rules, shut it down and
#                   restart it. Reinstalling without it leaves this as it is.
#   --proxmox       On a Proxmox VE node: report its virtual machines and containers. Makes the Proxmox VE user
#                   argus@pve with an API token that may only look (VM.Audit, Sys.Audit), for the agent alone.
#                   Running it again makes a new token.
#   --proxmox-actions
#                   Also let people who can see this machine in Argus start, shut down, restart and stop its
#                   guests (the token gains VM.PowerMgmt). Implies --proxmox. Reinstalling without it leaves
#                   this as it is.
#   --unifi URL     Read the UniFi network from the UniFi OS console at URL, such as https://192.168.1.1: internet
#                   traffic, devices and clients. One agent per network is enough. Needs --unifi-key-file.
#   --unifi-key-file FILE
#                   A file holding an API key made in UniFi Network under Settings > Control Plane > Integrations.
#                   The installer trusts the certificate the console presents now, so run it on the network.
#   --unifi-site SITE
#                   The UniFi site to read, by its short name; "default" unless you made more sites.
set -euo pipefail

SERVER=""
TOKEN=""
BINARY=""
DOCKER=false
CONTAINER_ACTIONS=false
POWER_ACTIONS=false
PROXMOX=false
PROXMOX_ACTIONS=false
UNIFI_URL=""
UNIFI_KEY_FILE=""
UNIFI_SITE=default
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
    --docker) DOCKER=true; shift ;;
    --container-actions) CONTAINER_ACTIONS=true; DOCKER=true; shift ;;
    --power-actions) POWER_ACTIONS=true; shift ;;
    --proxmox) PROXMOX=true; shift ;;
    --proxmox-actions) PROXMOX_ACTIONS=true; PROXMOX=true; shift ;;
    --unifi) UNIFI_URL="${2:-}"; shift 2 ;;
    --unifi-key-file) UNIFI_KEY_FILE="${2:-}"; shift 2 ;;
    --unifi-site) UNIFI_SITE="${2:-}"; shift 2 ;;
    *) fail "unknown option '$1' (expected --server, --token, --binary, --docker, --container-actions, --power-actions, --proxmox, --proxmox-actions, --unifi, --unifi-key-file or --unifi-site)" ;;
  esac
done

[ "$(id -u)" -eq 0 ] || fail "run this as root, for example with sudo"
command -v systemctl >/dev/null 2>&1 || fail "this installer needs systemd"
if $DOCKER && ! getent group docker >/dev/null 2>&1; then
  fail "--docker needs Docker installed: this machine has no docker group"
fi
if $PROXMOX && ! command -v pveum >/dev/null 2>&1; then
  fail "--proxmox needs Proxmox VE: this machine has no pveum"
fi
if [ -n "$UNIFI_URL" ]; then
  UNIFI_URL="${UNIFI_URL%/}"
  case "$UNIFI_URL" in
    https://*) ;;
    *) fail "--unifi must be the console's https address, e.g. https://192.168.1.1" ;;
  esac
  case "$UNIFI_URL$UNIFI_SITE" in
    *[[:space:]\"\\]*) fail "--unifi and --unifi-site cannot contain spaces, quotes or backslashes" ;;
  esac
  [ -n "$UNIFI_KEY_FILE" ] || fail "--unifi needs --unifi-key-file, a file holding the console's API key"
  [ -r "$UNIFI_KEY_FILE" ] || fail "cannot read the UniFi API key file $UNIFI_KEY_FILE"
  UNIFI_KEY="$(tr -d '[:space:]' < "$UNIFI_KEY_FILE")"
  [ -n "$UNIFI_KEY" ] || fail "the UniFi API key file $UNIFI_KEY_FILE is empty"
elif [ -n "$UNIFI_KEY_FILE" ]; then
  fail "--unifi-key-file needs --unifi, the console's address"
fi

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

# The agent's unit, and the updater's two, which install new agent versions when the web UI asks; with
# --power-actions, the two that power the machine off or restart it when the agent asks.
# Use the unit files shipped next to this script (archive installs), else fetch them from the server.
UNITS=(argus-agent.service argus-agent-update.service argus-agent-update.path)
if $POWER_ACTIONS; then
  UNITS+=(argus-agent-power.service argus-agent-power.path)
fi
SCRIPT_DIR="$(dirname -- "${BASH_SOURCE[0]:-$0}")"
for unit in "${UNITS[@]}"; do
  if [ -f "$SCRIPT_DIR/$unit" ]; then
    cp "$SCRIPT_DIR/$unit" "$WORK/$unit"
  else
    download "$SERVER/downloads/$unit" "$WORK/$unit" || fail "could not download $unit from $SERVER"
  fi
done

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

for unit in "${UNITS[@]}"; do
  install -m 0644 "$WORK/$unit" "/etc/systemd/system/$unit"
done

if $DOCKER; then
  step "Letting the agent watch Docker's containers"
  install -d -m 0755 "/etc/systemd/system/$SERVICE.service.d"
  printf '%s\n' "[Service]" \
    "# Docker's socket belongs to the docker group. Using it is as good as root on this machine." \
    "SupplementaryGroups=docker" > "/etc/systemd/system/$SERVICE.service.d/docker.conf"
  chmod 0644 "/etc/systemd/system/$SERVICE.service.d/docker.conf"
fi

if $CONTAINER_ACTIONS; then
  step "Letting Argus read container logs and start, stop and restart containers"
  printf '%s\n' "[Service]" \
    "# People who can see this machine in Argus may read its containers' logs and start, stop and restart them." \
    "Environment=ARGUS_CONTAINERACTIONS=true" > "/etc/systemd/system/$SERVICE.service.d/container-actions.conf"
  chmod 0644 "/etc/systemd/system/$SERVICE.service.d/container-actions.conf"
fi

if $POWER_ACTIONS; then
  step "Letting Argus shut down and restart this machine"
  install -d -m 0755 "/etc/systemd/system/$SERVICE.service.d"
  printf '%s\n' "[Service]" \
    "# People who can see this machine in Argus, and their alert rules, may shut it down and restart it." \
    "Environment=ARGUS_POWERACTIONS=true" > "/etc/systemd/system/$SERVICE.service.d/power-actions.conf"
  chmod 0644 "/etc/systemd/system/$SERVICE.service.d/power-actions.conf"
fi
if $PROXMOX; then
  # A token made again keeps the actions an earlier install allowed, unless they are asked for now.
  if grep -qs 'ARGUS_PROXMOXACTIONS=true' "/etc/systemd/system/$SERVICE.service.d/proxmox.conf"; then
    PROXMOX_ACTIONS=true
  fi
  PRIVILEGES="VM.Audit Sys.Audit"
  if $PROXMOX_ACTIONS; then
    step "Letting Argus read Proxmox VE's guests, and start, shut down, restart and stop them"
    PRIVILEGES="$PRIVILEGES VM.PowerMgmt"
  else
    step "Letting Argus read Proxmox VE's guests"
  fi
  pveum role modify ArgusAgent --privs "$PRIVILEGES" >/dev/null 2>&1 \
    || pveum role add ArgusAgent --privs "$PRIVILEGES" >/dev/null \
    || fail "could not make the Proxmox VE role ArgusAgent"
  pveum user add argus@pve --comment "Argus agent" >/dev/null 2>&1 || true
  pveum acl modify / --users argus@pve --roles ArgusAgent >/dev/null || fail "could not give argus@pve its role"
  pveum user token remove argus@pve agent >/dev/null 2>&1 || true
  SECRET="$(pveum user token add argus@pve agent --privsep 0 --comment "Argus agent" --output-format json \
    | sed -n 's/.*"value"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')"
  [ -n "$SECRET" ] || fail "could not make a Proxmox VE API token for argus@pve"
  # systemd reads this as root before starting the agent, so only root may read the token.
  (
    umask 077
    printf 'ARGUS_PROXMOXTOKEN=argus@pve!agent=%s\n' "$SECRET" > "$CONFIG_DIR/proxmox.env"
  )
  chown root:root "$CONFIG_DIR/proxmox.env"
  chmod 0600 "$CONFIG_DIR/proxmox.env"
  install -d -m 0755 "/etc/systemd/system/$SERVICE.service.d"
  {
    printf '%s\n' "[Service]" \
      "# The agent's Proxmox VE API token, to read this node's virtual machines and containers." \
      "EnvironmentFile=$CONFIG_DIR/proxmox.env"
    if $PROXMOX_ACTIONS; then
      printf '%s\n' "# People who can see this machine in Argus may start, shut down, restart and stop its guests." \
        "Environment=ARGUS_PROXMOXACTIONS=true"
    fi
  } > "/etc/systemd/system/$SERVICE.service.d/proxmox.conf"
  chmod 0644 "/etc/systemd/system/$SERVICE.service.d/proxmox.conf"
fi
if [ -n "$UNIFI_URL" ]; then
  step "Letting Argus read the UniFi network at $UNIFI_URL"
  # UniFi consoles sign their own certificates: trust the one the console presents now.
  FINGERPRINT=""
  if command -v openssl >/dev/null 2>&1; then
    HOSTPORT="${UNIFI_URL#https://}"
    HOSTPORT="${HOSTPORT%%/*}"
    case "$HOSTPORT" in *:*) ;; *) HOSTPORT="$HOSTPORT:443" ;; esac
    FINGERPRINT="$(openssl s_client -connect "$HOSTPORT" </dev/null 2>/dev/null \
      | openssl x509 -noout -fingerprint -sha256 2>/dev/null | sed -n 's/.*=//p')" || true
  fi
  if [ -z "$FINGERPRINT" ]; then
    echo "    Could not read the console's certificate; the agent's first report will show its fingerprint."
  fi
  # systemd reads this as root before starting the agent, so only root may read the key.
  (
    umask 077
    printf '%s\n' "ARGUS_UNIFIURL=$UNIFI_URL" "ARGUS_UNIFIAPIKEY=$UNIFI_KEY" "ARGUS_UNIFISITE=$UNIFI_SITE" \
      "ARGUS_UNIFIFINGERPRINT=$FINGERPRINT" > "$CONFIG_DIR/unifi.env"
  )
  chown root:root "$CONFIG_DIR/unifi.env"
  chmod 0600 "$CONFIG_DIR/unifi.env"
  install -d -m 0755 "/etc/systemd/system/$SERVICE.service.d"
  printf '%s\n' "[Service]" \
    "# The UniFi console the agent reads the network from, and its API key." \
    "EnvironmentFile=$CONFIG_DIR/unifi.env" > "/etc/systemd/system/$SERVICE.service.d/unifi.conf"
  chmod 0644 "/etc/systemd/system/$SERVICE.service.d/unifi.conf"
fi

systemctl daemon-reload
systemctl enable --now argus-agent-update.path >/dev/null
if $POWER_ACTIONS; then
  systemctl enable --now argus-agent-power.path >/dev/null
fi
systemctl enable --now "$SERVICE" >/dev/null

step "Done. The agent reports to $SERVER and shows up in the web UI within a minute."
echo "    Status: systemctl status $SERVICE"
echo "    Logs:   journalctl -u $SERVICE -f"
