# Argus — Implementation TODO

Argus is a self-hosted system monitoring platform:

- **Agents** (Linux & Windows) collect system metrics and push them to the server.
- **Server** (ASP.NET Core on .NET 10) authenticates agents, stores metrics in **TimescaleDB**,
  evaluates alert rules and serves both the REST API and the web UI.
- **Web UI** (React + TypeScript, served by the server) — users log in and monitor their systems.
- The server side runs with `docker compose` on any Linux machine.

This file is the master task list. Tasks are worked top to bottom; each one is checked off and
committed when it is done. New tasks discovered along the way are added in the right phase.

## Key decisions

| Area | Choice | Why |
| --- | --- | --- |
| Backend | .NET 10 (LTS), ASP.NET Core Minimal APIs | Modern, fast, supported until Nov 2028 |
| Database | PostgreSQL 18 + TimescaleDB 2.x | Hypertables, compression, retention and continuous aggregates for time series, plus normal relational tables (users, hosts, alerts) in one database |
| Data access | EF Core 10 + Npgsql for relational data; Dapper / raw SQL for time-series queries and bulk ingest | EF for the model & migrations, SQL where TimescaleDB features matter |
| UI auth | ASP.NET Core Identity, HttpOnly cookie | Same-origin SPA, no tokens exposed to JavaScript |
| Agent auth | Enrollment token → per-host API key (stored hashed) | Keys revocable per host; tokens can expire / be limited |
| Frontend | React + TypeScript + Vite, Mantine UI, TanStack Query, uPlot charts | Rich components, fast charts for dense time series |
| Live updates | SignalR | Built into ASP.NET Core |
| Agent | .NET 10 worker service, self-contained single-file binary, systemd unit / Windows Service | One language across the stack, shared contracts |
| Email (later) | SMTP via MailKit, Mailpit for local testing | Works with any mail server / relay |

---

## Phase 0 — Repository foundation

- [x] `.gitignore`, `.gitattributes`, `.editorconfig`
- [x] `global.json` (pin .NET 10 SDK), `Directory.Build.props` (nullable, analyzers, versioning), `Directory.Packages.props` (central package management)
- [x] Solution `Argus.slnx` with `Argus.Contracts`, `Argus.Server`, `Argus.Agent`, `Argus.Server.Tests`, `Argus.Agent.Tests`
- [x] `docs/architecture.md` — components, data flow, data model overview

## Phase 1 — Shared contracts (`Argus.Contracts`)

- [x] Agent API DTOs: registration request/response, `SystemInfo` inventory, metrics batch, host sample, filesystem sample, network interface sample, process info, server-pushed agent settings
- [x] Source-generated `JsonSerializerContext` (trim-safe) shared by agent and server
- [x] Serialization round-trip tests

## Phase 2 — Server foundation (`Argus.Server`)

- [x] Minimal API skeleton: feature folders, strongly typed options with validation, JSON console logging
- [x] ProblemDetails, global exception handler, built-in minimal API validation
- [x] Health checks: `/health/live` and `/health/ready` (database)
- [x] OpenAPI document + Scalar API reference (Development only)
- [x] `deploy/docker-compose.dev.yml` with TimescaleDB for local development
- [x] `ArgusDbContext` (EF Core + Npgsql, snake_case naming) and `dotnet-ef` local tool
- [x] Automatic migrations on startup (configurable) + TimescaleDB extension check
- [x] Data protection keys persisted in the database

## Phase 3 — Authentication & users

- [x] ASP.NET Core Identity (Guid keys, `Admin`/`User` roles) + initial migration
- [x] Cookie auth tuned for the SPA: 401/403 instead of redirects, secure cookie flags, sliding expiration
- [x] `GET /api/auth/status` and `POST /api/auth/setup` (first-run admin creation)
- [x] `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/auth/me` with account lockout
- [x] Optional self-registration (`Argus:Auth:AllowRegistration`)
- [x] `POST /api/account/password` (change password) and profile update
- [x] Admin user management endpoints (list, create, change role, disable/enable, reset password, delete)
- [x] CSRF defence: custom header required on unsafe cookie-authenticated requests
- [x] Security headers middleware and rate limiting on auth endpoints
- [x] Integration test infrastructure (WebApplicationFactory + Testcontainers TimescaleDB) and auth tests

## Phase 4 — Hosts, enrollment & agent authentication

- [x] `Host` entity (inventory, owner, tags, notes, last seen, agent key hash) + migration
- [x] Secure token / key generation and SHA-256 hashing helpers
- [x] `EnrollmentToken` entity + endpoints (create — shown once, list, revoke; expiry and max uses)
- [x] Agent API-key authentication scheme (hashed lookup with short cache)
- [x] `POST /api/agent/v1/register` — exchange an enrollment token for host id + agent key (re-links an existing host by machine id)
- [x] `PUT /api/agent/v1/inventory` — system info updates
- [x] Enrollment & agent auth integration tests

## Phase 5 — Time-series storage & ingestion

- [x] `host_metrics` hypertable (wide row: CPU, memory, swap, load, disk IO, network, processes, uptime)
- [x] `filesystem_metrics` and `network_metrics` hypertables
- [x] Latest top-process snapshot per host
- [x] `POST /api/agent/v1/metrics` — validated batch ingestion, idempotent `INSERT … SELECT unnest(…) ON CONFLICT DO NOTHING`, updates last seen
- [x] Ingestion safeguards: batch and body size limits, timestamp sanity window, per-agent rate limiting
- [x] Compression policies on raw hypertables
- [x] Continuous aggregates: 5-minute and 1-hour rollups (avg / max)
- [x] Retention policies (configurable raw / 5m / 1h retention)
- [x] Ingestion integration tests

## Phase 6 — Agent core (`Argus.Agent`)

- [x] Worker host with systemd and Windows Service integration, console logging
- [x] Configuration: JSON file + environment variables + CLI, per-OS default paths, validation
- [x] State store for host id / agent key with restrictive file permissions
- [x] CLI commands: `run`, `register`, `collect` (print one sample), `version`
- [x] Collector abstractions and sample assembly pipeline
- [x] Linux: CPU (`/proc/stat`), memory & swap (`/proc/meminfo`), load (`/proc/loadavg`), uptime
- [x] Linux: filesystems (`/proc/mounts` + `statvfs`), disk IO (`/proc/diskstats`), network (`/proc/net/dev`)
- [x] Windows: CPU (`GetSystemTimes`), memory (`GlobalMemoryStatusEx`), filesystems, disk IO, network, uptime
- [x] Cross-platform process collector (count + top N by CPU and memory)
- [x] System info collector (OS, kernel, CPU model, cores, memory, IPs, machine id) for Linux & Windows
- [x] Registration flow using the enrollment token, persisted state
- [x] Bounded sample buffer with retry & exponential backoff
- [x] HTTP transport (timeouts, gzip, user agent) and applying server-pushed settings
- [x] Unit tests with `/proc` fixtures, delta calculations and buffer behaviour
- [x] Manual end-to-end check: agent → local server → rows in TimescaleDB

## Phase 7 — Query API

- [x] `GET /api/hosts` — hosts with online/offline status and latest metric snapshot
- [x] `GET /api/hosts/{id}`, `PATCH /api/hosts/{id}` (name, tags, notes), `DELETE /api/hosts/{id}`
- [x] Metrics query service: picks raw / 5m / 1h source by range, `time_bucket` downsampling to a target point count
- [x] `GET /api/hosts/{id}/metrics` — host time series
- [x] `GET /api/hosts/{id}/filesystems` (latest + history) and `GET /api/hosts/{id}/network` (per-interface history)
- [x] `GET /api/hosts/{id}/processes` — latest top processes
- [x] `GET /api/dashboard/summary` — fleet overview (counts, busiest hosts, fullest disks; alert counts come with Phase 8)
- [x] Owner-scoped authorization (admins see everything) + tests

## Phase 8 — Alerting engine

- [x] `AlertRule` entity: metric, operator, threshold, duration, severity, scope (all hosts / host / tag), resource filter, enabled
- [x] `Alert` entity: firing → resolved lifecycle, observed value, acknowledgement; one open alert per rule/host/resource
- [x] Alert rule CRUD endpoints + validation
- [x] Default rules created for new users (CPU, memory, disk, host offline)
- [x] Evaluator: sustained-threshold evaluation over a time window for host metrics
- [x] Per-filesystem disk rules and host-offline rules
- [x] Background evaluation service (interval, per-rule error isolation)
- [x] Alert endpoints: list / filter / paginate, acknowledge
- [x] Evaluator unit tests + firing / resolving integration test
- [x] Dashboard summary: active alert counts by severity

## Phase 9 — Real-time updates

- [x] SignalR hub (`/hubs/live`) with cookie auth and per-user groups
- [x] Broadcast latest metric snapshots on ingest
- [x] Broadcast alert fired / resolved and host online / offline events

## Phase 10 — Web UI (`web/`)

- [x] Vite + React + TypeScript scaffold, ESLint, Prettier, dev proxy to the API
- [x] Theme and app shell (sidebar navigation, header, light / dark)
- [x] API client (fetch wrapper, CSRF header, error handling) + TanStack Query
- [x] Auth screens: login, first-run setup, registration; route guards and session handling
- [x] Dashboard: fleet summary, host tiles with live status, active alerts
- [x] Hosts list: search, status / tag filters, sorting
- [x] Time-series chart component (range picker, tooltips, units)
- [x] Host detail: overview (system info, current values) + metric charts
- [x] Host detail: filesystems, network interfaces, top processes
- [x] Host settings: rename, tags, notes, delete
- [x] "Add system" flow: create enrollment token, show Linux / Windows install commands
- [x] Enrollment tokens page
- [x] Alerts page: active & history, filters, acknowledge
- [x] Alert rules page: list, create / edit, enable / disable
- [x] Live updates via SignalR
- [x] Account page (change password) and admin users page
- [x] Loading skeletons, empty states, error boundary, 404
- [x] Route-level code splitting (the main bundle is over 500 kB)
- [x] Server hosts the built SPA (static files, fallback routing, cache headers)

## Phase 11 — Agent packaging & installation

- [x] Publish settings for `linux-x64`, `linux-arm64`, `win-x64` self-contained single-file builds
- [x] systemd unit (dedicated user, sandboxing)
- [x] Linux `install.sh` / `uninstall.sh`
- [x] Windows `install.ps1` / `uninstall.ps1` (service, recovery actions, ACLs)
- [x] `build/package-agent.sh` producing release archives
- [x] Server serves agent binaries and install scripts at `/downloads/*` (the UI's install commands are part of the "Add system" flow)
- [ ] Trimmed agent builds (a trimmed linux-x64 build is 14 MB instead of 39 MB; needs a full run against a server first)
- [ ] Test `install.sh` and the sandboxed systemd unit end to end on a clean Linux VM
- [ ] Verify the Windows agent (collectors, service install) on a real Windows machine

## Phase 12 — Docker & deployment

- [x] Multi-stage `Dockerfile` (web build → server publish → agent builds → non-root runtime image)
- [x] Production `deploy/docker-compose.yml` + `.env.example` (server, TimescaleDB, volumes, health checks)
- [x] Optional Caddy reverse proxy with automatic HTTPS (compose profile)
- [ ] Try the Caddy profile with a real domain (the compose configuration is validated, HTTPS itself untested)
- [x] Forwarded headers and public URL configuration
- [x] `docs/deployment.md` (install, upgrade, backup / restore)
- [x] End-to-end smoke test: compose up → setup → enroll agent → metrics visible

## Phase 13 — Errors & anomalies

- [x] Agent: failed service detection (systemd failed units / stopped automatic Windows services)
- [x] Server: store service status and expose it via the API
- [x] "Service failed" alert rule type
- [x] Anomaly detection rule type (deviation from a rolling baseline)
- [x] UI: services panel and the new rule types

## Phase 14 — Notifications, email & reports

- [ ] Notification dispatcher (background queue, retries) wired to alert events
- [ ] SMTP email sender (MailKit) + configuration; Mailpit in the dev compose file
- [ ] Alert emails (fired / resolved) with HTML templates
- [ ] Webhook channel (generic JSON, Slack / Discord compatible)
- [ ] Notification channel management API + UI (with "send test")
- [ ] Scheduled reports (daily / weekly summary email)

## Phase 15 — CI & polish

- [x] GitHub Actions: build & test .NET, lint / test / build web, agent packages
- [x] CI builds the Docker image (once Phase 12 adds it)
- [ ] Frontend unit tests (Vitest)
- [ ] Configuration reference and API docs
- [ ] Final README pass

---

## Future ideas (not scheduled)

- Organizations / teams with shared systems
- Single sign-on (OIDC) and two-factor authentication
- Per-core CPU, temperatures, GPU and container (Docker) metrics
- Synthetic checks (HTTP, ping, TCP port)
- Log collection and search
- macOS agent
- Prometheus / OpenTelemetry export
