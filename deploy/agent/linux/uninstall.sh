#!/usr/bin/env bash
# Removes the Argus agent. With --purge its configuration, identity and user are deleted too.
# The host stays listed in the web UI (with its history) until you delete it there.
set -euo pipefail

PURGE=false
case "${1:-}" in
  "") ;;
  --purge) PURGE=true ;;
  *) echo "usage: uninstall.sh [--purge]" >&2; exit 2 ;;
esac

[ "$(id -u)" -eq 0 ] || { echo "argus-agent uninstall: run this as root, for example with sudo" >&2; exit 1; }

systemctl disable --now argus-agent-update.path argus-agent >/dev/null 2>&1 || true
rm -f /etc/systemd/system/argus-agent.service /etc/systemd/system/argus-agent-update.service \
  /etc/systemd/system/argus-agent-update.path
rm -rf /etc/systemd/system/argus-agent.service.d
systemctl daemon-reload
rm -rf /opt/argus-agent

if $PURGE; then
  rm -rf /etc/argus-agent /var/lib/argus-agent
  userdel argus-agent >/dev/null 2>&1 || true
fi

echo "Argus agent removed."
