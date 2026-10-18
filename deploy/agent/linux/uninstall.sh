#!/usr/bin/env bash
# Removes the Argus agent. With --purge its configuration, identity and user are deleted too, and on
# Proxmox VE its user argus@pve and role ArgusAgent.
# The host stays listed in the web UI (with its history) until you delete it there.
set -euo pipefail

PURGE=false
case "${1:-}" in
  "") ;;
  --purge) PURGE=true ;;
  *) echo "usage: uninstall.sh [--purge]" >&2; exit 2 ;;
esac

[ "$(id -u)" -eq 0 ] || { echo "argus-agent uninstall: run this as root, for example with sudo" >&2; exit 1; }

systemctl disable --now argus-agent-update.path argus-agent-power.path argus-agent >/dev/null 2>&1 || true
rm -f /etc/systemd/system/argus-agent.service /etc/systemd/system/argus-agent-update.service \
  /etc/systemd/system/argus-agent-update.path /etc/systemd/system/argus-agent-power.service \
  /etc/systemd/system/argus-agent-power.path
rm -rf /etc/systemd/system/argus-agent.service.d
systemctl daemon-reload
rm -rf /opt/argus-agent

if $PURGE; then
  rm -rf /etc/argus-agent /var/lib/argus-agent
  userdel argus-agent >/dev/null 2>&1 || true
  if command -v pveum >/dev/null 2>&1; then
    pveum user delete argus@pve >/dev/null 2>&1 || true
    pveum role delete ArgusAgent >/dev/null 2>&1 || true
  fi
fi

echo "Argus agent removed."
