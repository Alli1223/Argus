# Argus

Argus is a self-hosted tool for keeping an eye on your own machines. Small agents on Linux and
Windows report what each machine is doing; a server stores the history, raises alerts and
sends notifications; and a web app shows it all.

- **Hosts at a glance:** CPU, memory, swap, load, disk and network, live and over time, with
  filesystems, network interfaces, temperatures, the busiest processes and failed services for each
  machine, and every machine's temperature sensors on one page.
- **Docker containers** on every machine: state, health checks, restart loops and crashes, with each
  container's CPU, memory and network over time.
- **Alert rules** on fixed thresholds, on readings that stray from a host's usual level, on hosts
  that stop reporting, on services that fail and on containers that go down or restart in a loop. Rules can cover every host, one host or a tag.
- **Notifications** by email, Slack, Discord or webhook, with retries, plus daily and weekly
  reports.
- **Accounts** for several people, each with their own hosts, and administrators who see them all.
- **Easy to run:** one Docker Compose file for the server and its database, optional automatic
  HTTPS with Caddy, and install commands for agents generated in the web app.
- **Updates from the web app** for the server and the agents. The server backs up its database first
  and goes back to the previous version by itself if the new one does not start.

## Quick start

On a Linux machine with Docker:

```sh
git clone https://github.com/Alli1223/Argus.git
cd Argus
git checkout v0.5.1                   # the latest release
cp deploy/.env.example deploy/.env    # then set POSTGRES_PASSWORD, ARGUS_PUBLIC_URL and ARGUS_VERSION
docker compose -f deploy/docker-compose.yml up -d
```

Open Argus in a browser and create the administrator account. Then open **Add a system**, create an
enrollment token, and run the install command it shows on each machine you want to watch.

[The deployment guide](docs/deployment.md) covers HTTPS, running behind your own proxy, email,
upgrades and backups.

## How it fits together

- **Agent** ([`src/Argus.Agent`](src/Argus.Agent)): a self-contained .NET program that runs as a
  systemd or Windows service. It takes a reading every 15 seconds and keeps readings while the
  server is out of reach.
- **Server** ([`src/Argus.Server`](src/Argus.Server)): ASP.NET Core on .NET 10. It stores readings
  in PostgreSQL with TimescaleDB, evaluates alert rules, sends notifications and serves the web app
  and the agent downloads.
- **Web app** ([`web`](web)): React, Mantine and uPlot, with live updates over SignalR.
- **Contracts** ([`src/Argus.Contracts`](src/Argus.Contracts)): what agents and the server say to
  each other.

[Architecture](docs/architecture.md) goes into more detail.

## Development

You need the .NET 10 SDK, Node.js 24 and Docker.

```sh
# The database, and Mailpit to catch emails (read them at http://localhost:8025)
docker compose -f deploy/docker-compose.dev.yml up -d

# The server, on http://localhost:5080
dotnet run --project src/Argus.Server

# The web app, on http://localhost:5173, passing API calls to the server
cd web
npm install
npm run dev
```

To try an agent against it, build one and register it with a token from **Add a system**:

```sh
dotnet run --project src/Argus.Agent -- register --help
dotnet run --project src/Argus.Agent -- collect    # one reading, printed as JSON
```

### Tests

```sh
dotnet test                  # server and agent; the server tests start TimescaleDB in Docker
cd web && npm test           # web app
cd web && npm run lint && npm run format:check
```

Continuous integration runs these, builds the agent packages and the Docker image, and starts the
image for a smoke test.

## Documentation

- [Deployment](docs/deployment.md): installing, HTTPS, upgrades, backups
- [Releasing](docs/releasing.md): publishing a new version
- [Configuration reference](docs/configuration.md): every server and agent setting
- [HTTP API](docs/api.md): for scripts and integrations
- [Architecture](docs/architecture.md)
- [UI design](docs/ui-design.md)
- [TODO](TODO.md): what is done and what is next
