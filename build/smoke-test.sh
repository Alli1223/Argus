#!/usr/bin/env bash
# End-to-end check of the production setup: start deploy/docker-compose.yml, set up the first
# account, enroll a real agent (downloaded from the server itself) and wait for its metrics.
#
#   build/smoke-test.sh
#
# Needs Docker with Compose, curl and python3, on an x86-64 Linux machine (the agent it runs is the
# linux-x64 build). Everything it starts is removed again at the end, volumes included.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="argus-smoke-$$"
PORT="${ARGUS_SMOKE_PORT:-18080}"
BASE="http://127.0.0.1:$PORT"
WORK="$(mktemp -d)"
JAR="$WORK/cookies"
AGENT_PID=""

step() { echo "==> $*"; }
fail() { echo "smoke test failed: $*" >&2; exit 1; }

compose() {
  docker compose --project-name "$PROJECT" --file "$ROOT/deploy/docker-compose.yml" --env-file "$WORK/env" "$@"
}

cleanup() {
  [ -n "$AGENT_PID" ] && kill "$AGENT_PID" 2>/dev/null || true
  if [ "${KEEP:-}" != "1" ]; then
    compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

[ "$(uname -s)-$(uname -m)" = "Linux-x86_64" ] || fail "this script runs the linux-x64 agent, so it needs x86-64 Linux"

json() { python3 -c "import json, sys; data = json.load(sys.stdin); print($1)"; }
api() { curl -fsS -b "$JAR" -c "$JAR" -H "X-Argus-Csrf: 1" -H "Content-Type: application/json" "$@"; }

step "Building the server and updater images"
docker build --quiet -t argus-smoke/server:smoke "$ROOT" >/dev/null
docker build --quiet -t argus-smoke/updater:smoke "$ROOT/deploy/updater" >/dev/null

cat > "$WORK/env" <<EOF
ARGUS_IMAGE=argus-smoke/server
ARGUS_UPDATER_IMAGE=argus-smoke/updater
ARGUS_VERSION=smoke
POSTGRES_PASSWORD=smoke-$(od -An -N12 -tx1 /dev/urandom | tr -d ' \n')
ARGUS_PUBLIC_URL=$BASE
ARGUS_HTTP_BIND=127.0.0.1:$PORT
EOF

step "Starting the stack ($PROJECT)"
compose up --detach --wait --wait-timeout 300 >/dev/null || { compose logs server | tail -40; fail "the stack did not become healthy"; }

step "Checking the web app and the server"
curl -fsS "$BASE/" | grep -q '<div id="root">' || fail "the web app is not served at /"
[ "$(curl -fsS "$BASE/api/info" | json 'data["name"]')" = "Argus" ] || fail "/api/info did not answer"

step "Creating the first account"
api -d '{"email":"admin@example.com","password":"correct horse battery","displayName":"Admin"}' "$BASE/api/auth/setup" >/dev/null

step "Creating an enrollment token"
TOKEN="$(api -d '{"name":"smoke test","expiresInHours":1,"tags":["smoke"]}' "$BASE/api/enrollment-tokens" | json 'data["token"]')"

step "Downloading the agent from the server"
curl -fsS -o "$WORK/argus-agent" "$BASE/downloads/agent/linux-x64/argus-agent"
chmod +x "$WORK/argus-agent"
curl -fsS "$BASE/downloads/install.sh" | grep -q "systemctl" || fail "the install script is not served"

step "Running the agent"
ARGUS_SERVERURL="$BASE" ARGUS_ENROLLMENTTOKEN="$TOKEN" ARGUS_STATEDIRECTORY="$WORK/agent-state" \
  ARGUS_COLLECTIONINTERVALSECONDS=5 "$WORK/argus-agent" run > "$WORK/agent.log" 2>&1 &
AGENT_PID=$!

step "Waiting for the host and its metrics"
for _ in $(seq 1 30); do
  HOSTS="$(api "$BASE/api/hosts")"
  if [ "$(echo "$HOSTS" | json 'sum(1 for h in data if h["latest"] and "smoke" in h["tags"])')" -ge 1 ]; then
    HOST_ID="$(echo "$HOSTS" | json '[h["id"] for h in data if "smoke" in h["tags"]][0]')"
    POINTS="$(api "$BASE/api/hosts/$HOST_ID/metrics" | json 'sum(1 for v in data["series"]["cpu"] if v is not None)')"
    echo "    host $HOST_ID reports; $POINTS CPU reading(s) in the last hour"
    [ "$POINTS" -ge 1 ] || fail "the host has no stored metrics"
    step "Smoke test passed"
    exit 0
  fi
  sleep 2
done

tail -20 "$WORK/agent.log" >&2
fail "no metrics arrived from the agent within a minute"
