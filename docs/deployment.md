# Deploying Argus

Argus runs as three containers: the server, which also serves the web app and the agent downloads;
TimescaleDB; and the updater, which installs new releases when an administrator asks for one in the
web app. An optional fourth, Caddy, puts HTTPS in front of them. Everything is described in
[`deploy/docker-compose.yml`](../deploy/docker-compose.yml), which runs the images each release
publishes on GitHub's container registry.

## What you need

- A Linux machine, x86-64 or ARM64, that your other machines can reach. One or two CPU cores and
  2 GB of memory are plenty for a few dozen hosts; disk use depends on how many hosts you watch
  and how long you keep their history (see [Keeping history](#keeping-history)).
- Docker Engine with the Compose plugin, which Docker's own packages include. To build the images
  yourself you also need Buildx; some distributions ship it separately (on Arch Linux it is
  `docker-buildx`).
- For HTTPS with the bundled Caddy: a domain name whose DNS points at the machine, and ports 80
  and 443 open to the internet so Let's Encrypt can issue a certificate.

## Install

```sh
git clone https://github.com/Alli1223/Argus.git
cd Argus
git checkout v0.5.0    # the latest release, from https://github.com/Alli1223/Argus/releases
cp deploy/.env.example deploy/.env
```

Staying on `main` instead gets changes that have not been released yet.

Edit `deploy/.env`. At least set a long `POSTGRES_PASSWORD` and `ARGUS_PUBLIC_URL`, the address
people and agents will use (it appears in the agents' install commands), and set `ARGUS_VERSION` to
the release you checked out. Then start Argus:

```sh
docker compose -f deploy/docker-compose.yml up -d
```

When `docker compose -f deploy/docker-compose.yml ps` shows the server as healthy, open Argus in a
browser. The first visit asks you to create the administrator account.

To watch your first machine, open **Add a system**: create an enrollment token and run the
install command it shows on that machine.

## HTTPS with Caddy

In `deploy/.env`:

```sh
ARGUS_DOMAIN=argus.example.com
ARGUS_PUBLIC_URL=https://argus.example.com
# Only Caddy needs to reach the server directly.
ARGUS_HTTP_BIND=127.0.0.1:8080
COMPOSE_PROFILES=caddy
```

Run `docker compose -f deploy/docker-compose.yml up -d` again. Caddy obtains and renews the
certificate on its own and keeps it in the `caddy-data` volume.

Without HTTPS, agents send their keys and people send their passwords unencrypted. When you add a
system and the install command would reach Argus over plain HTTP from another machine, the web app
says so.

## Behind your own reverse proxy

Forward everything, websockets on `/hubs` included, to the server's port, and send the
`X-Forwarded-For` and `X-Forwarded-Proto` headers. The server believes those headers only from
proxies it is told about, so tell it where yours connects from. With Compose, put this in
`deploy/docker-compose.override.yml`:

```yaml
services:
  server:
    environment:
      Argus__Proxy__TrustedProxies__0: 192.168.1.10     # the proxy's address as the server sees it
      # Argus__Proxy__TrustedNetworks__0: 10.0.0.0/24   # or the network it connects from
```

## Configuration

The server reads its settings from environment variables named after its configuration keys, with
`__` between the parts: `Argus:Retention:RawDays` becomes `Argus__Retention__RawDays`. Set them in
`deploy/docker-compose.override.yml` as above. These are the ones you are most likely to change;
[the configuration reference](configuration.md) lists them all, with the agent's settings.

| Setting | Default | What it does |
| --- | --- | --- |
| `Argus:PublicUrl` | none | The address used in install commands and in links from notifications. Without it, install commands use the page's own address. |
| `Argus:Auth:AllowRegistration` | `false` | Lets anyone who can reach Argus create an account. |
| `Argus:Agents:CollectionIntervalSeconds` | `15` | How often agents take a reading. |
| `Argus:Agents:OfflineAfterSeconds` | `90` | How long a host can stay silent before it counts as offline. |
| `Argus:Alerts:EvaluationIntervalSeconds` | `30` | How often alert rules are checked. |
| `Argus:Smtp:Host`, `Argus:Smtp:Port` | none, `587` | The mail server for email notifications. Without a host, no emails are sent. Administrators can set this under **Settings** in the web app instead, which then takes the place of these. |
| `Argus:Smtp:Security` | `Auto` | `Auto` (TLS on port 465, otherwise STARTTLS when offered), `None`, `StartTls` or `SslOnConnect`. |
| `Argus:Smtp:Username`, `Argus:Smtp:Password` | none | The mail server sign-in, if it needs one. |
| `Argus:Smtp:From`, `Argus:Smtp:FromName` | none, `Argus` | Who emails come from. Required with a host. |
| `Argus:Reports:SendHourUtc`, `Argus:Reports:WeeklyDay` | `7`, `Monday` | When daily and weekly reports go out (UTC). Each covers the day or week up to that hour. |
| `Argus:Notifications:MaxAttempts` | `6` | How many times a notification is tried before Argus gives up. Retries wait 1, 5 and 15 minutes, then 1 and 4 hours. |
| `Argus:Retention:RawDays` | `14` | How long individual readings are kept. |
| `Argus:Retention:FiveMinuteDays` | `90` | How long five-minute averages are kept. |
| `Argus:Retention:HourlyDays` | `730` | How long hourly averages are kept. |
| `Argus:RateLimits:AuthPermitsPerMinute` | `10` | Sign-in attempts allowed per address per minute. |
| `Argus:RateLimits:NotificationTestPermitsPerMinute` | `5` | Test notifications each person may send per minute. |
| `Argus:Proxy:TrustedProxies`, `Argus:Proxy:TrustedNetworks` | none | Proxies whose forwarded headers are believed. |

### Keeping history

Charts read raw readings for the last six hours, five-minute averages up to a week, and hourly
averages beyond that. Readings older than two days are compressed. Disk use grows with the number
of hosts, how often they report and how long each kind of history is kept. Retention changes apply
the next time the server starts.

## Upgrade

Administrators see a notice in Argus when a new release is out. Open **Settings**, read what the
release brings, and choose **Update**. The updater then:

1. downloads the release's server image,
2. backs up the database into `deploy/backups/`, readable only by the owner of the `deploy`
   directory (it keeps the latest five),
3. sets `ARGUS_VERSION` in `deploy/.env` and restarts the server as the new version,
4. waits for the new server to report healthy, and for it to stay that way.

The page follows each step. Argus is unavailable for a minute or two while the server restarts;
agents keep their readings and send them once it is back. When the update is done, reload the page to
use the new web app.

If the new version does not become healthy, the updater puts `deploy/.env` back and starts the
previous version again, and the page says why, with the new version's last log lines. If the new
version had already changed the database, the backup is restored first, so anything recorded in the
few minutes between the backup and the rollback is lost. If going back fails too, Argus stays stopped
and the page names the backup to [restore](#restore) by hand.

The updater only replaces the server. When a release changes `deploy/docker-compose.yml` itself, its
notes say so; upgrade that release by hand.

Once the server runs the new version, update the agents from the **Hosts** page.

### The updater and Docker

To start and stop containers the updater needs the Docker socket, which is as good as root on the
machine. It is kept small: it has no network and no ports, it reads nothing from the server but a
version number, it only installs images from the repository named in the Compose file, and never an
older version than the one running. If you would rather no container had that access, delete the
`updater` service from the Compose file; **Settings** then shows the commands to upgrade by hand.

### By hand

Take a [backup](#back-up) first: a schema update cannot be undone except by restoring one. Then,
here for 0.5.0:

```sh
git fetch --tags
git checkout v0.5.0
sed -i 's/^ARGUS_VERSION=.*/ARGUS_VERSION=0.5.0/' deploy/.env    # add the line if it is not there
docker compose -f deploy/docker-compose.yml up -d
```

### From 0.3.0 or earlier

Until 0.4.0 the Compose file built Argus from source and had no updater. Upgrade to 0.4.0 or later
by hand once, as above, making sure `deploy/.env` has an `ARGUS_VERSION` line. Docker downloads the
released images, starts the updater, and later releases can be installed from **Settings**. The
image built before (`argus:latest`) is no longer used; `docker image rm argus:latest` removes it.

### Building the images yourself

To run your own build, give it a name and version of your own in `deploy/.env`:

```sh
docker build -t argus-local/server:dev .
docker build -t argus-local/updater:dev deploy/updater
```

```sh
ARGUS_IMAGE=argus-local/server
ARGUS_UPDATER_IMAGE=argus-local/updater
ARGUS_VERSION=dev
```

Updating from **Settings** then downloads released images from `argus-local/server`, which do not
exist, so upgrade by hand.

## Updating agents

When a release has a newer agent, hosts running an older one say so, and you can update one host or
all of them from the web app. The server downloads the agent for each platform from the GitHub
release and keeps it only if it matches the release's `SHA256SUMS`; agents then fetch it from the
server with their key, check it again, and replace themselves:

- **Linux:** the agent runs sandboxed as an unprivileged user and cannot replace its own program. It
  asks the `argus-agent-update` systemd unit, which runs as root, fetches the offered update from the
  server named in `/etc/argus-agent/agent.json`, checks it, installs it and restarts the agent.
- **Windows:** the service starts a copy of itself that stops the service, swaps the program and
  starts it again.

If the new agent does not keep running, the previous one goes back and the host shows why. Agents
installed from a release before 0.2.0 have no updater: run their install command once more.

## Back up

```sh
docker compose -f deploy/docker-compose.yml exec -T db pg_dump -U argus -Fc argus > argus-$(date +%F).dump
```

The dump holds everything: accounts, hosts and their agents' keys, history, alerts and the keys
that protect sign-in cookies. Keep it somewhere safe. `pg_dump` warns about circular foreign-key
constraints on TimescaleDB's own tables; that is expected and harmless.

## Restore

Restore into the same TimescaleDB version the dump came from (the image tag in the compose file).

```sh
compose="docker compose -f deploy/docker-compose.yml"
$compose stop server
$compose exec -T db psql -U argus -d postgres -c "DROP DATABASE argus WITH (FORCE);" -c "CREATE DATABASE argus;"
$compose exec -T db psql -U argus -d argus -c "CREATE EXTENSION IF NOT EXISTS timescaledb;" -c "SELECT timescaledb_pre_restore();"
$compose exec -T db pg_restore -U argus -d argus < argus-2026-09-15.dump
$compose exec -T db psql -U argus -d argus -c "SELECT timescaledb_post_restore();"
$compose start server
```

People stay signed in and agents keep reporting, since their keys come back with the rest.

## Health

The server answers `/health/live` (the process is up) and `/health/ready` (and can reach the
database). The container's own health check uses the latter, so `docker compose ps` shows whether
Argus is ready.

## Remove Argus

`docker compose -f deploy/docker-compose.yml down` stops and removes the containers and keeps the
data. Add `--volumes` to delete the database and Caddy's certificates as well.
