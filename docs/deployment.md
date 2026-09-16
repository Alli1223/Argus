# Deploying Argus

Argus runs as two containers: the server, which also serves the web app and the agent downloads,
and TimescaleDB. An optional third, Caddy, puts HTTPS in front of them. Everything is described in
[`deploy/docker-compose.yml`](../deploy/docker-compose.yml).

## What you need

- A Linux machine, x86-64 or ARM64, that your other machines can reach. One or two CPU cores and
  2 GB of memory are plenty for a few dozen hosts; disk use depends on how many hosts you watch
  and how long you keep their history (see [Keeping history](#keeping-history)).
- Docker Engine with the Compose and Buildx plugins. Docker's own packages include both; some
  distributions ship Buildx separately (on Arch Linux it is `docker-buildx`).
- For HTTPS with the bundled Caddy: a domain name whose DNS points at the machine, and ports 80
  and 443 open to the internet so Let's Encrypt can issue a certificate.

## Install

```sh
git clone https://github.com/Alli1223/Argus.git
cd Argus
cp deploy/.env.example deploy/.env
```

Edit `deploy/.env`. At least set a long `POSTGRES_PASSWORD` and `ARGUS_PUBLIC_URL`, the address
people and agents will use (it appears in the agents' install commands). Then build and start:

```sh
docker compose -f deploy/docker-compose.yml up -d --build
```

The first build takes a few minutes; it compiles the web app, the server and the agents for Linux
and Windows. When `docker compose -f deploy/docker-compose.yml ps` shows the server as healthy,
open Argus in a browser. The first visit asks you to create the administrator account.

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
`deploy/docker-compose.override.yml` as above. The defaults are in
[`appsettings.json`](../src/Argus.Server/appsettings.json).

| Setting | Default | What it does |
| --- | --- | --- |
| `Argus:PublicUrl` | none | The address used in install commands. Without it, the page's own address is used. |
| `Argus:Auth:AllowRegistration` | `false` | Lets anyone who can reach Argus create an account. |
| `Argus:Agents:CollectionIntervalSeconds` | `15` | How often agents take a reading. |
| `Argus:Agents:OfflineAfterSeconds` | `90` | How long a host can stay silent before it counts as offline. |
| `Argus:Alerts:EvaluationIntervalSeconds` | `30` | How often alert rules are checked. |
| `Argus:Smtp:Host`, `Argus:Smtp:Port` | none, `587` | The mail server for email notifications. Without a host, no emails are sent. |
| `Argus:Smtp:Security` | `Auto` | `Auto` (TLS on port 465, otherwise STARTTLS when offered), `None`, `StartTls` or `SslOnConnect`. |
| `Argus:Smtp:Username`, `Argus:Smtp:Password` | none | The mail server sign-in, if it needs one. |
| `Argus:Smtp:From`, `Argus:Smtp:FromName` | none, `Argus` | Who emails come from. Required with a host. |
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

```sh
git pull
docker compose -f deploy/docker-compose.yml up -d --build
```

The server updates the database schema when it starts. Agents keep their readings while the
server restarts and send them once it is back. A browser tab left open from before the upgrade
asks to be reloaded when it opens a page that has changed.

Take a backup first. A schema update cannot be undone except by restoring one.

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
