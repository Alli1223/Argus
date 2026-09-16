#!/bin/sh
# The Argus updater: installs the server releases administrators choose in the web app.
#
# It runs as the `updater` service in deploy/docker-compose.yml, with the Docker socket, the Compose
# project directory mounted at /deploy, the updates volume it shares with the server, and no network.
# The server writes request.json naming a version; the updater never takes anything else from it. For
# each request it:
#
#   1. fetches the release's server image (Docker does the download),
#   2. backs up the database with pg_dump into backups/ next to the compose file,
#   3. sets ARGUS_VERSION in .env and recreates the server container,
#   4. waits for the server to report healthy and stay that way.
#
# When the new server does not come up, it puts .env back, restores the backup if the new version had
# changed the database, and starts the previous version again. Progress goes to status.json, which the
# server shows in the web app, and a heartbeat to updater.json.
# Single-quoted $names are meant for jq or for the shell in the database container.
# shellcheck disable=SC2016
set -eu

UPDATES_DIR=${UPDATES_DIR:-/updates}
DEPLOY_MOUNT=${DEPLOY_MOUNT:-/deploy}
SERVER_SERVICE=${SERVER_SERVICE:-server}
DB_SERVICE=${DB_SERVICE:-db}
SERVER_UID=${SERVER_UID:-1654}
HEALTH_TIMEOUT_SECONDS=${HEALTH_TIMEOUT_SECONDS:-300}
SETTLE_SECONDS=${SETTLE_SECONDS:-30}
KEEP_BACKUPS=${KEEP_BACKUPS:-5}
POLL_SECONDS=${POLL_SECONDS:-5}
UPDATER_VERSION=${UPDATER_VERSION:-dev}

STATUS="$UPDATES_DIR/status.json"
REQUEST="$UPDATES_DIR/request.json"
HEARTBEAT="$UPDATES_DIR/updater.json"
ENV_FILE="$DEPLOY_MOUNT/.env"
BACKUPS="$DEPLOY_MOUNT/backups"
# .env holds secrets, so its copy stays with the backups rather than on the volume the server can read.
ENV_BEFORE="$BACKUPS/env-before-update"

# Image, status, health and restart count of a container, as wait_for_server compares them.
SERVER_STATE='{{.Config.Image}}|{{.State.Status}}|{{if .State.Health}}{{.State.Health.Status}}{{end}}|{{.RestartCount}}'

problem=""
project=""
workdir=""

now() { date -u +%Y-%m-%dT%H:%M:%SZ; }

say() { printf '%s %s\n' "$(now)" "$*"; }

is_version() { printf '%s' "$1" | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+$'; }

# Whether version $1 is later than version $2.
is_newer() {
  awk -v a="$1" -v b="$2" 'BEGIN {
    split(a, x, "."); split(b, y, ".")
    for (i = 1; i <= 3; i++) { if (x[i] + 0 > y[i] + 0) exit 0; if (x[i] + 0 < y[i] + 0) exit 1 }
    exit 1 }'
}

# Writes stdin to a file in one step, so the server never reads half of it.
write_file() {
  cat > "$1.tmp" && chmod 0644 "$1.tmp" && mv -f "$1.tmp" "$1"
}

heartbeat() {
  jq -n --arg version "$UPDATER_VERSION" --arg at "$(now)" --arg problem "$problem" \
    '{updaterVersion: $version, heartbeatAt: $at, problem: (if $problem == "" then null else $problem end)}' \
    | write_file "$HEARTBEAT"
}

# Applies a jq filter to status.json, with $at set to the current time. Progress counts as a heartbeat.
status() {
  filter=$1
  shift
  jq --arg at "$(now)" "$@" "$filter" "$STATUS" | write_file "$STATUS"
  heartbeat
}

step() {
  say "$2"
  status '.state = $state | .log += [{at: $at, message: $message}]' --arg state "$1" --arg message "$2"
}

note() {
  say "$1"
  status '.log += [{at: $at, message: $message}]' --arg message "$1"
}

finish() {
  say "$2"
  status '.state = $state | .finishedAt = $at | .error = (if $error == "" then null else $error end)
    | .log += [{at: $at, message: $message}]' --arg state "$1" --arg message "$2" --arg error "${3:-}"
}

# Finds the Compose project this container belongs to, so updates run against the same files. Compose
# resolves paths in the project on this side, so the project directory is linked here under its own name.
discover() {
  problem=""
  self=$(hostname)
  if ! labels=$(docker inspect --format '{{json .Config.Labels}}' "$self" 2>/dev/null); then
    problem="The updater cannot use Docker. Mount /var/run/docker.sock into it."
    return 1
  fi

  project=$(printf '%s' "$labels" | jq -r '."com.docker.compose.project" // empty')
  workdir=$(printf '%s' "$labels" | jq -r '."com.docker.compose.project.working_dir" // empty')
  files=$(printf '%s' "$labels" | jq -r '."com.docker.compose.project.config_files" // empty')
  if [ -z "$project" ] || [ -z "$workdir" ] || [ -z "$files" ]; then
    problem="The updater has to be started with Docker Compose, as part of Argus."
    return 1
  fi

  source=$(docker inspect --format '{{json .Mounts}}' "$self" \
    | jq -r --arg target "$DEPLOY_MOUNT" '.[] | select(.Destination == $target) | .Source')
  if [ "$source" != "$workdir" ]; then
    problem="Mount the Compose project directory ($workdir) into the updater at $DEPLOY_MOUNT."
    return 1
  fi

  if [ ! -e "$workdir" ]; then
    mkdir -p "$(dirname "$workdir")"
    ln -s "$DEPLOY_MOUNT" "$workdir"
  fi

  COMPOSE_PROJECT_NAME=$project
  COMPOSE_FILE=$(printf '%s' "$files" | tr ',' ':')
  export COMPOSE_PROJECT_NAME COMPOSE_FILE
}

# Compose itself, reading variables from .env only: anything set here would override it.
compose() {
  env -u ARGUS_VERSION -u ARGUS_IMAGE -u ARGUS_UPDATER_IMAGE docker compose --project-directory "$workdir" "$@"
}

# Runs a command in the database container, where the database's own variables are set.
in_db() { compose exec -T "$DB_SERVICE" sh -c "$1"; }

server_container() { compose ps --all --quiet "$SERVER_SERVICE" 2>/dev/null | head -n 1; }

env_value() {
  [ -f "$ENV_FILE" ] || return 0
  sed -n "s/^$1=//p" "$ENV_FILE" | tail -n 1 | tr -d '"'"'"
}

# Sets a variable in .env, keeping the file itself so its owner and permissions stay the same.
set_env_value() {
  touch "$ENV_FILE"
  { grep -v "^$1=" "$ENV_FILE" || true; printf '%s=%s\n' "$1" "$2"; } > "$ENV_FILE.new"
  cat "$ENV_FILE.new" > "$ENV_FILE"
  rm -f "$ENV_FILE.new"
}

# The version the server runs: its image's version label, or ARGUS_VERSION when the image has none.
running_version() {
  container=$(server_container)
  version=""
  if [ -n "$container" ]; then
    image=$(docker inspect --format '{{.Image}}' "$container")
    version=$(docker image inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$image" 2>/dev/null || true)
  fi

  if ! is_version "$version"; then
    version=$(env_value ARGUS_VERSION)
  fi

  if is_version "$version"; then printf '%s' "$version"; else printf 'unknown'; fi
}

migrations() {
  in_db 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -tAc "SELECT migration_id FROM __ef_migrations_history ORDER BY 1"'
}

# Waits for the server container to run the expected image and be healthy, then checks it stays that
# way. A new server that reports itself unhealthy, or has crashed and been restarted, fails straight away.
wait_for_server() {
  expected_image=$1
  state=""
  deadline=$(($(date +%s) + HEALTH_TIMEOUT_SECONDS))
  while [ "$(date +%s)" -lt "$deadline" ]; do
    heartbeat
    container=$(server_container)
    if [ -n "$container" ]; then
      state=$(docker inspect --format "$SERVER_STATE" "$container" 2>/dev/null || true)
      case "$state" in
        "$expected_image|running|healthy|0")
          sleep "$SETTLE_SECONDS"
          [ "$(docker inspect --format "$SERVER_STATE" "$container" 2>/dev/null || true)" = "$state" ] && return 0
          note "The server stopped being healthy right after starting."
          return 1
          ;;
        "$expected_image|"*"|unhealthy|"*)
          note "The server reported itself unhealthy."
          return 1
          ;;
        "$expected_image|"*"|"[1-9]*)
          note "The server stopped and had to be restarted."
          return 1
          ;;
      esac
    fi
    sleep 3
  done

  note "The server was not healthy within $HEALTH_TIMEOUT_SECONDS seconds (${state:-no container})."
  return 1
}

# The server image Compose would run, for ARGUS_VERSION $1 or else the version in .env.
server_image() {
  if [ $# -gt 0 ]; then
    env -u ARGUS_IMAGE -u ARGUS_UPDATER_IMAGE ARGUS_VERSION="$1" docker compose --project-directory "$workdir" config --format json
  else
    compose config --format json
  fi | jq -r --arg service "$SERVER_SERVICE" '.services[$service].image // empty'
}

restore_database() {
  backup=$1
  compose stop "$SERVER_SERVICE" &&
    in_db 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d postgres -c "DROP DATABASE \"$POSTGRES_DB\" WITH (FORCE);" -c "CREATE DATABASE \"$POSTGRES_DB\";"' &&
    in_db 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "CREATE EXTENSION IF NOT EXISTS timescaledb;" -c "SELECT timescaledb_pre_restore();"' || return 1

  # pg_restore reports TimescaleDB's internal objects as errors it ignored, so its exit code says little;
  # the migration history afterwards shows whether the restore worked.
  in_db 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$backup" > /dev/null 2>&1 || true
  in_db 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT timescaledb_post_restore();"' > /dev/null
}

prune_backups() {
  # Names sort by time within each version, so keep the newest by modification time.
  # shellcheck disable=SC2012 # the names are the updater's own, without spaces or newlines
  ls -1t "$BACKUPS"/argus-*.dump 2>/dev/null | tail -n +"$((KEEP_BACKUPS + 1))" | while read -r old; do rm -f "$old"; done
}

rollback() {
  reason=$1 from=$2 backup=$3 before=$4
  step rolling-back "Going back to Argus $from: $reason"

  logs=$(compose logs --no-color --no-log-prefix --tail 40 "$SERVER_SERVICE" 2>&1 | tail -c 6000 || true)
  status '.serverLog = (if $logs == "" then null else $logs end)' --arg logs "$logs"

  cat "$ENV_BEFORE" > "$ENV_FILE"

  after=$(migrations 2>/dev/null || printf 'unreadable')
  if [ "$after" != "$before" ]; then
    note "The new version had changed the database, so the backup goes back first."
    if ! restore_database "$backup" || [ "$(migrations 2>/dev/null || true)" != "$before" ]; then
      finish failed "Restoring the backup did not work, so Argus is stopped." \
        "$reason Restoring the backup then failed. Restore $backup by hand (see docs/deployment.md)."
      return
    fi
  fi

  # Only images already here: pulling could fetch something other than what ran before.
  previous_image=$(server_image)
  if compose up --detach --no-deps --pull never "$SERVER_SERVICE" > /dev/null 2>&1 && wait_for_server "$previous_image"; then
    finish rolled-back "Argus $from is running again." "$reason"
  else
    finish failed "Argus $from did not start again either." \
      "$reason Going back to $from did not work either. The backup from before the update is $backup."
  fi
}

update() {
  id=$1 to=$2 requested_by=$3
  from=$(running_version)
  jq -n --arg id "$id" --arg from "$from" --arg to "$to" --arg by "$requested_by" --arg at "$(now)" \
    '{id: $id, from: $from, to: $to, requestedBy: $by, state: "starting", startedAt: $at, finishedAt: null,
      error: null, backup: null, serverLog: null, log: []}' | write_file "$STATUS"

  if ! is_version "$to"; then
    finish failed "The request did not name a version." "Updates can only go to a release version, such as 1.2.3."
    return
  fi

  if [ "$from" != unknown ] && ! is_newer "$to" "$from"; then
    finish failed "Argus $from is not older than $to." "Argus already runs $from, which is not older than $to."
    return
  fi

  if [ -z "$(server_container)" ]; then
    finish failed "There is no server container to update." "The server is not running, so there is nothing to update."
    return
  fi

  if ! image=$(server_image "$to") || [ -z "$image" ]; then
    finish failed "Reading the Compose files failed." "The updater could not read the Compose files."
    return
  fi

  step downloading "Downloading $image."
  if ! docker image inspect "$image" > /dev/null 2>&1 && ! docker pull --quiet "$image" > /dev/null 2>&1; then
    finish failed "Downloading $image failed." "Downloading $image failed. Check that the release has a server image and that this machine can reach its registry."
    return
  fi

  step backing-up "Backing up the database."
  if ! before=$(migrations); then
    finish failed "Reading the database failed." "The updater could not read the database, so nothing was changed."
    return
  fi

  owner=$(stat -c %u:%g "$DEPLOY_MOUNT")
  mkdir -p "$BACKUPS" && chown "$owner" "$BACKUPS" && chmod 0700 "$BACKUPS"
  backup="$BACKUPS/argus-$from-before-$to-$(date -u +%Y%m%dT%H%M%SZ).dump"
  # The dump holds sign-in keys and agent key hashes, so only the owner of the Compose files can read it.
  if ! in_db 'pg_dump -U "$POSTGRES_USER" -Fc "$POSTGRES_DB"' > "$backup.partial" \
    || ! in_db 'pg_restore --list' < "$backup.partial" > /dev/null; then
    rm -f "$backup.partial"
    finish failed "Backing up the database failed." "Backing up the database failed, so nothing was changed."
    return
  fi
  mv "$backup.partial" "$backup"
  chown "$owner" "$backup" && chmod 0600 "$backup"
  status '.backup = $backup' --arg backup "${workdir}/backups/$(basename "$backup")"
  note "Saved the backup as backups/$(basename "$backup")."

  touch "$ENV_FILE"
  cp "$ENV_FILE" "$ENV_BEFORE" && chown "$owner" "$ENV_BEFORE" && chmod 0600 "$ENV_BEFORE"

  step deploying "Starting Argus $to."
  set_env_value ARGUS_VERSION "$to"
  if ! compose up --detach --no-deps --pull never "$SERVER_SERVICE" > /dev/null 2>&1; then
    rollback "Starting Argus $to failed." "$from" "$backup" "$before"
    return
  fi

  step verifying "Waiting for Argus $to to report healthy."
  if ! wait_for_server "$image"; then
    rollback "Argus $to did not start properly." "$from" "$backup" "$before"
    return
  fi

  rm -f "$ENV_BEFORE"
  prune_backups
  finish succeeded "Argus $to is running."
}

# A status still in progress means the updater itself stopped part way, for instance when the machine
# restarted. What happened to the server is unknown, so say so rather than guess.
recover() {
  [ -f "$STATUS" ] || return 0
  state=$(jq -r '.state // empty' "$STATUS" 2>/dev/null || true)
  case "$state" in
    succeeded | rolled-back | failed | "") return 0 ;;
  esac
  backup=$(jq -r '.backup // "none was made"' "$STATUS")
  finish failed "The updater stopped part way through the update." \
    "The updater stopped part way through the update, so check which version Argus runs. The backup from before it is $backup."
}

main() {
  say "Argus updater $UPDATER_VERSION starting"
  mkdir -p "$UPDATES_DIR"
  recover

  while true; do
    # The server runs as an unprivileged user and writes its requests here.
    chown "$SERVER_UID" "$UPDATES_DIR" 2>/dev/null || true
    chmod 0755 "$UPDATES_DIR"

    if discover && [ -f "$REQUEST" ]; then
      request_id=$(jq -r '.id // empty' "$REQUEST" 2>/dev/null || true)
      handled_id=$(jq -r '.id // empty' "$STATUS" 2>/dev/null || true)
      if printf '%s' "$request_id" | grep -Eq '^[A-Za-z0-9-]{1,64}$' && [ "$request_id" != "$handled_id" ]; then
        heartbeat
        update "$request_id" "$(jq -r '.version // empty' "$REQUEST")" "$(jq -r '.requestedBy // "" | .[0:200]' "$REQUEST")"
      fi
    fi

    heartbeat
    sleep "$POLL_SECONDS"
  done
}

main "$@"
