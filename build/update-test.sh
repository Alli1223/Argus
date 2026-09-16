#!/usr/bin/env bash
# End-to-end check of server updates: start deploy/docker-compose.yml with the updater, then have the
# updater install stand-in releases built from this tree.
#
#   build/update-test.sh
#
#   0.9.0  the starting point
#   0.9.1  a good release, which has to be installed
#   0.9.2  a release that never becomes healthy, which has to be rolled back
#   0.9.3  the same, installed over a database it changes, so rolling back has to restore the backup
#   0.9.9  a release without an image, which has to fail without touching anything
#
# Needs Docker with Compose, curl and jq. Set ARGUS_TEST_SERVER_IMAGE to an image built from this
# tree to skip building one. Everything it starts is removed again at the end, volumes included.
set -euo pipefail

ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="argus-update-test-$$"
PORT="${ARGUS_UPDATE_TEST_PORT:-18090}"
BASE="http://127.0.0.1:$PORT"
WORK="$(mktemp -d)"
DEPLOY="$WORK/deploy"
JAR="$WORK/cookies"
SERVER=argus-update-test/server
UPDATER=argus-update-test/updater

step() { echo "==> $*"; }
fail() { echo "update test failed: $*" >&2; exit 1; }

compose() { docker compose --project-name "$PROJECT" --project-directory "$DEPLOY" -f "$DEPLOY/docker-compose.yml" -f "$DEPLOY/test.yml" "$@"; }

cleanup() {
  status=$?
  if [ "$status" -ne 0 ]; then
    compose logs --no-color --tail 60 updater >&2 || true
    compose exec -T updater cat /updates/status.json >&2 || true
  fi
  if [ "${KEEP:-}" != "1" ]; then
    compose down --volumes --remove-orphans >/dev/null 2>&1 || true
    rm -rf "$WORK"
  fi
}
trap cleanup EXIT

api() { curl -fsS -b "$JAR" -c "$JAR" -H "X-Argus-Csrf: 1" -H "Content-Type: application/json" "$@"; }
env_version() { sed -n 's/^ARGUS_VERSION=//p' "$DEPLOY/.env"; }
server_image() { docker inspect --format '{{.Config.Image}}' "$(compose ps --quiet server)"; }

# Hands the updater a request the way the server writes one, and waits for the update to end.
update_to() {
  local id="test-${1//./-}-$RANDOM"
  jq -n --arg id "$id" --arg version "$1" '{id: $id, version: $version, requestedBy: "update-test", requestedAt: (now | todate)}' \
    | compose exec -T updater sh -c 'cat > /updates/request.json.tmp && mv /updates/request.json.tmp /updates/request.json'

  for _ in $(seq 1 150); do
    STATUS="$(compose exec -T updater cat /updates/status.json 2>/dev/null || true)"
    if [ "$(jq -r '.id' <<<"$STATUS" 2>/dev/null)" = "$id" ] \
      && jq -e '.state | IN("succeeded", "rolled-back", "failed")' <<<"$STATUS" >/dev/null; then
      echo "    $(jq -r '.state + ": " + (.log[-1].message)' <<<"$STATUS")"
      return 0
    fi
    sleep 2
  done
  fail "the update to $1 did not end within five minutes"
}

expect_state() { [ "$(jq -r '.state' <<<"$STATUS")" = "$1" ] || fail "expected the update to end $1: $STATUS"; }

step "Building stand-in releases"
if [ -n "${ARGUS_TEST_SERVER_IMAGE:-}" ]; then
  docker tag "$ARGUS_TEST_SERVER_IMAGE" "$SERVER:base"
else
  docker build --quiet -t "$SERVER:base" "$ROOT" >/dev/null
fi
docker build --quiet -t "$UPDATER:0.9.0" "$ROOT/deploy/updater" >/dev/null
for version in 0.9.0 0.9.1 0.9.2 0.9.3; do
  broken=""
  if [ "$version" = 0.9.2 ] || [ "$version" = 0.9.3 ]; then
    # Unhealthy once the server has had time to start and log something, as a broken release would be.
    broken='HEALTHCHECK --interval=3s --timeout=2s --start-period=15s --retries=2 CMD ["dotnet", "/app/no-such-program.dll"]'
  fi
  printf 'FROM %s\nLABEL org.opencontainers.image.version=%s\n%s\n' "$SERVER:base" "$version" "$broken" \
    | docker build --quiet -t "$SERVER:$version" - >/dev/null
  docker tag "$UPDATER:0.9.0" "$UPDATER:$version"
done

step "Starting Argus 0.9.0 with the updater ($PROJECT)"
mkdir -p "$DEPLOY"
cp "$ROOT/deploy/docker-compose.yml" "$DEPLOY/"
cat > "$DEPLOY/.env" <<EOF
ARGUS_IMAGE=$SERVER
ARGUS_UPDATER_IMAGE=$UPDATER
ARGUS_VERSION=0.9.0
POSTGRES_PASSWORD=update-test-$(od -An -N12 -tx1 /dev/urandom | tr -d ' \n')
ARGUS_PUBLIC_URL=$BASE
ARGUS_HTTP_BIND=127.0.0.1:$PORT
EOF
# A network Docker picks, so the test does not collide with an Argus already on this machine, and an
# updater that waits less than it would for a real release.
cat > "$DEPLOY/test.yml" <<'EOF'
networks:
  argus:
    ipam: !reset {}
services:
  updater:
    environment:
      POLL_SECONDS: "1"
      SETTLE_SECONDS: "5"
      HEALTH_TIMEOUT_SECONDS: "60"
EOF
compose up --detach --wait --wait-timeout 300 db server >/dev/null || { compose logs server | tail -40; fail "the stack did not become healthy"; }
compose up --detach updater >/dev/null

step "Checking the server sees the updater"
api -d '{"email":"admin@example.com","password":"correct horse battery","displayName":"Admin"}' "$BASE/api/auth/setup" >/dev/null
api -d '{"email":"admin@example.com","password":"correct horse battery","rememberMe":false}' "$BASE/api/auth/login" >/dev/null
for _ in $(seq 1 30); do
  [ "$(api "$BASE/api/updates/server" | jq -r '.available')" = true ] && break
  sleep 1
done
[ "$(api "$BASE/api/updates/server" | jq -r '.available')" = true ] || fail "the server does not see the updater: $(api "$BASE/api/updates/server")"
[ "$(compose exec -T updater stat -c %u /updates)" = 1654 ] || fail "the server cannot write to the updates volume"

step "Installing a good release"
update_to 0.9.1
expect_state succeeded
[ "$(server_image)" = "$SERVER:0.9.1" ] || fail "the server runs $(server_image), not 0.9.1"
[ "$(env_version)" = 0.9.1 ] || fail ".env names $(env_version), not 0.9.1"
backup="$(ls "$DEPLOY"/backups/argus-0.9.0-before-0.9.1-*.dump 2>/dev/null)" || fail "no backup was made"
if [ ! -r "$backup" ] || [ ! -s "$backup" ]; then fail "no readable backup was made"; fi
[ "$(stat -c %a "$backup")" = 600 ] || fail "the backup can be read by others"
[ "$(api "$BASE/api/updates/server" | jq -r '.lastRun.state')" = succeeded ] || fail "the server does not show the update"
curl -fsS "$BASE/health/ready" >/dev/null || fail "Argus is not ready after the update"

step "Rolling back a release that does not start"
update_to 0.9.2
expect_state rolled-back
[ "$(server_image)" = "$SERVER:0.9.1" ] || fail "the server runs $(server_image) after rolling back"
[ "$(env_version)" = 0.9.1 ] || fail ".env names $(env_version) after rolling back"
jq -e '.serverLog | length > 0' <<<"$STATUS" >/dev/null || fail "the rollback did not keep the new version's log"
if jq -e 'any(.log[]; .message | contains("backup goes back"))' <<<"$STATUS" >/dev/null; then
  fail "the backup was restored although the database had not changed"
fi

step "Restoring the backup when the failed release changed the database"
# Make the database look one migration older, so the next release changes it when it starts.
# shellcheck disable=SC2016 # the variables are the database container's own
compose exec -T db sh -c 'psql -v ON_ERROR_STOP=1 -q -U "$POSTGRES_USER" -d "$POSTGRES_DB"' >/dev/null <<'EOF'
DROP MATERIALIZED VIEW temperature_metrics_1h;
DROP TABLE temperature_metrics;
DELETE FROM __ef_migrations_history WHERE migration_id LIKE '%_AddTemperatures';
EOF
update_to 0.9.3
expect_state rolled-back
jq -e 'any(.log[]; .message | contains("backup goes back"))' <<<"$STATUS" >/dev/null || fail "the backup was not restored"
[ "$(server_image)" = "$SERVER:0.9.1" ] || fail "the server runs $(server_image) after restoring"
# The restored database is the one from before the update, so 0.9.1 applies the migration again.
server_log="$(compose logs --no-color server)"
grep -q "AddTemperatures" <<<"$server_log" || fail "the restored database still had the new migration"
curl -fsS "$BASE/health/ready" >/dev/null || fail "Argus is not ready after restoring"

step "Refusing releases it cannot or should not install"
update_to 0.9.9
expect_state failed
update_to 0.9.0
expect_state failed
if [ "$(server_image)" != "$SERVER:0.9.1" ] || [ "$(env_version)" != 0.9.1 ]; then fail "a refused update changed the server"; fi

step "Update test passed"
