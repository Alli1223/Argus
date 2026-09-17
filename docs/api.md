# HTTP API

The web app uses this API, and so can scripts. In development the server also publishes an OpenAPI
document at `/openapi/v1.json` and an interactive reference at `/scalar`, with every request and
response shape.

## Signing in

The API uses the same cookie session as the web app. Sign in with `POST /api/auth/login`, keep the
`argus_session` cookie it sets, and send it with later requests.

Every request that changes something (anything but `GET`, `HEAD` and `OPTIONS`) must also carry an
`X-Argus-Csrf` header, with any value. Browsers do not let other sites add that header, which is how
Argus refuses forged requests. Without it the answer is `403`.

```sh
curl -c cookies -H 'X-Argus-Csrf: 1' -H 'Content-Type: application/json' \
  -d '{"email":"me@example.com","password":"…"}' https://argus.example.com/api/auth/login
curl -b cookies https://argus.example.com/api/hosts
```

People see and manage their own hosts. Administrators see every host and alert and manage user
accounts. Alert rules and notification channels are personal, even for administrators.

## Requests and responses

- Bodies are JSON with camelCase names. Enumerations are strings, such as `"Critical"`.
- Times are ISO 8601 in UTC, such as `2026-09-16T12:00:00Z`.
- Errors are [problem details](https://www.rfc-editor.org/rfc/rfc9457): a `title`, a `detail` and
  the `status`. Invalid input answers `400` with an `errors` object of messages per field.
- Sign-in and similar endpoints are rate limited; see
  [Rate limits](configuration.md#rate-limits).

## Endpoints

All paths start with `/api`.

### Server and account

| Method and path | What it does |
| --- | --- |
| `GET /info` | The server's name, version and public address. No sign-in needed. |
| `GET /auth/status` | Whether the first account still has to be set up, and whether registration is open. No sign-in needed. |
| `POST /auth/setup` | Creates the first account, an administrator: `email`, `password`, `displayName`. Only works once. |
| `POST /auth/register` | Creates an account when registration is open. |
| `POST /auth/login` | Signs in: `email`, `password`, `rememberMe`. |
| `POST /auth/logout` | Signs out. |
| `GET /auth/me` | The signed-in person. |
| `PUT /account/profile` | Changes your `displayName`. |
| `POST /account/password` | Changes your password: `currentPassword`, `newPassword`. |

### Users (administrators)

| Method and path | What it does |
| --- | --- |
| `GET /users` | Every account. |
| `POST /users` | Creates an account: `email`, `displayName`, `password`, `role` (`Admin` or `User`). |
| `PUT /users/{id}` | Changes `displayName` and `role`. |
| `POST /users/{id}/disable`, `POST /users/{id}/enable` | Stops or restores someone's access. |
| `POST /users/{id}/reset-password` | Sets a new password: `newPassword`. |
| `DELETE /users/{id}` | Deletes an account with its hosts, enrollment tokens, alert rules, alerts and notification channels. |

### Hosts

| Method and path | What it does |
| --- | --- |
| `GET /hosts` | Your hosts with their status and latest readings. |
| `GET /hosts/{id}` | One host, with what the machine is. |
| `PATCH /hosts/{id}` | Changes any of `displayName`, `tags` and `notes`. |
| `DELETE /hosts/{id}` | Deletes the host and its history. Its agent can no longer report. |
| `GET /hosts/{id}/metrics` | CPU, memory, swap, load, disk and network history. See [History queries](#history-queries). |
| `GET /hosts/{id}/filesystems` | Every filesystem in the latest reading. |
| `GET /hosts/{id}/filesystems/history` | Space used per filesystem over time. |
| `GET /hosts/{id}/network` | Traffic per network interface over time. |
| `GET /hosts/{id}/containers` | The host's Docker containers from its newest report: `checkedAt` (null if its agent has never reported any), `engineVersion`, a `problem` when the agent could not read Docker, `actionsEnabled`, and `containers`, each with its `state`, `health`, `restartCount`, `restartsLastHour`, `exitCode`, times, Compose `composeProject` and `composeService`, `ports` and newest `usage`. |
| `GET /hosts/{id}/containers/{name}` | One container, with its latest `events` (such as `started`, `stopped`, `restarted`, `unhealthy`), newest first. |
| `GET /hosts/{id}/containers/{name}/metrics` | A container's CPU (`cpu`, `cpuMax`), memory (`memory`, `memoryLimit`) and traffic (`netRx`, `netTx`) over time. |
| `POST /hosts/{id}/containers/{name}/start`, `/stop`, `/restart` | Starts, stops or restarts the container through its host's agent. Answers `204` once done; `409` when the machine does not allow container actions or its agent is not connected; `502` with Docker's reason when it refused; `504` when the agent did not answer in time. |
| `GET /hosts/{id}/containers/{name}/logs` | The container's newest log `lines` (`time`, `stream` of `stdout` or `stderr`, `text`), read through its agent: `tail` lines, 200 by default and at most 5000. `truncated` says whether older lines were left out. Same errors as above. |
| `GET /containers` | Every host you can see that reports containers, with its containers. |
| `GET /hosts/{id}/temperatures` | Each temperature sensor over time, in degrees Celsius. |
| `GET /hosts/temperatures` | The temperature history of every host that reported temperatures in the range, by name: `hostId`, `displayName` and `history`. |
| `GET /hosts/{id}/processes` | The busiest processes in the latest reading. |
| `GET /hosts/{id}/services` | Services failing in the latest check, and when it was. |
| `POST /hosts/{id}/agent-update` | Asks the host's agent to update to the latest release. `409` when there is nothing to update to. |
| `DELETE /hosts/{id}/agent-update` | Withdraws the request, and forgets why the last attempt failed. |
| `POST /hosts/agent-updates` | Asks every host you can see whose agent is out of date. Answers how many were asked. |
| `GET /dashboard/summary` | Host counts, active alerts by severity, and the busiest hosts. |

A host's `agentUpdate` is `null`, or says which newer version is `available`, which one was
`requested` and when, and the `error` of the last failed attempt.

#### History queries

The history endpoints take `from` and `to` (ISO 8601, defaulting to the last hour) and `points`,
roughly how many points to return (10–2000, default 300). A range may cover up to two years. The
answer is columnar: `time` holds Unix seconds and `series` holds one array per measure, with `null`
where there was no reading. `resolution` and `bucketSeconds` say how the readings were averaged.

Temperature series are keyed `{device}/{sensor}`, such as `coretemp/Package id 0` or
`nvme0/Composite`, split at the first slash. Their points are the highest reading in each bucket
rather than the average, so short spikes still show over long ranges.

### Adding systems

| Method and path | What it does |
| --- | --- |
| `GET /enrollment-tokens` | Your enrollment tokens. |
| `POST /enrollment-tokens` | Creates one: `name`, and optionally `expiresInHours`, `maxUses` and `tags` for the hosts that use it. The token itself is only returned now. |
| `DELETE /enrollment-tokens/{id}` | Revokes a token. Hosts that already registered with it keep working. |

### Alerts

| Method and path | What it does |
| --- | --- |
| `GET /alerts` | Alerts, newest first. Filters: `status` (`Firing`, `Resolved`), `hostId`, `severity`; pages: `page`, `pageSize` (up to 200). |
| `GET /alerts/summary` | Firing alerts by severity. |
| `POST /alerts/{id}/acknowledge` | Marks a firing alert as seen. |
| `GET /alert-rules` | Your alert rules. |
| `GET /alert-rules/{id}` | One rule. |
| `POST /alert-rules` | Creates a rule. See below. |
| `PUT /alert-rules/{id}` | Replaces a rule. Changing what it measures resolves its open alerts. |
| `DELETE /alert-rules/{id}` | Deletes a rule and resolves its alerts. They stay in the history. |

A rule has a `name`, a `metric`, a `condition`, an `operator` (`Above` or `Below`), a `threshold`,
a `durationSeconds`, a `severity` (`Info`, `Warning` or `Critical`), `enabled`, and optionally a
`hostId` or a `tag` to narrow it.

- `metric` is one of `CpuUsage`, `MemoryUsage`, `SwapUsage`, `LoadPerCore`, `DiskIoUtilization`,
  `NetworkReceive`, `NetworkTransmit`, `DiskUsage`, `InodeUsage`, `HostOffline`, `ServiceFailed`,
  `ContainerDown` or `ContainerRestarts`. Percentages are 0–100, network rates bytes per second.
- `DiskUsage` and `InodeUsage` rules can be narrowed to one mount point, `ServiceFailed` rules to
  one service, and container rules to one container by name, with `resourceFilter`.
- `HostOffline`, `ServiceFailed` and `ContainerDown` have no threshold: they fire once the state has
  lasted the duration. A container is down when it is restarting, dead, unhealthy or has crashed; a
  rule that names the container also counts it down when it was stopped, paused or never started.
- `ContainerRestarts` fires when Docker restarted a container more than `threshold` times (a whole
  number) within `durationSeconds`, at least 60.
- `condition` is `Threshold` (the default) or `Anomaly`. Anomaly rules work on the host-wide
  metrics, compare with each host's usual level over the past week, and take the threshold as a
  number of standard deviations (1–10) and a duration of at least 5 minutes.

### Updates (administrators)

| Method and path | What it does |
| --- | --- |
| `GET /updates` | This server's version, the latest release with its notes, whether it is newer, and when and how the last check went. |
| `POST /updates/check` | Checks GitHub now. |
| `GET /updates/server` | Whether Argus can install releases itself (`available`, or why not in `unavailable`), a `pendingVersion` the updater has not started on yet, and its latest update in `lastRun`: `from`, `to`, `state`, `error`, `backup`, the new version's `serverLog` when it was rolled back, and a `log` of steps. |
| `POST /updates/server` | Asks the updater to install the latest release: `version`. Answers `202`, or `409` with the reason when it cannot, such as when an update is already running. |

### Notification channels

| Method and path | What it does |
| --- | --- |
| `GET /notification-channels` | Your channels, each with how its latest notification went. |
| `GET /notification-channels/support` | Whether the server can send email, and when reports go out. |
| `GET /notification-channels/{id}` | One channel. |
| `POST /notification-channels` | Creates a channel. See below. |
| `PUT /notification-channels/{id}` | Replaces a channel. |
| `DELETE /notification-channels/{id}` | Deletes a channel and any notifications still waiting on it. |
| `POST /notification-channels/{id}/test` | Sends a test message straight away. `204` when it went out; `502` with the reason when it did not. |

A channel has a `name`, a `kind` and a `target`: for `Email`, up to 10 addresses separated by
commas; for `Slack`, `Discord` and `Webhook`, the webhook URL. `minimumSeverity` (default
`Warning`) and `notifyOnResolved` (default `true`) choose which alerts it passes on, and
`dailyReport` and `weeklyReport` add reports. `enabled` switches it on and off.

`Webhook` channels receive a POST with JSON like this when an alert fires or resolves:

```json
{
  "event": "alert.fired",
  "alert": {
    "id": "01a0aa6d-8818-7184-8117-044c3a6aaf73",
    "title": "CPU usage on web-1 above 90%",
    "severity": "Critical",
    "status": "firing",
    "reading": "97%, threshold 90%",
    "rule": "Hot CPU",
    "hostId": "01a0aa6d-864f-7c6e-8840-23474b485dd1",
    "hostName": "web-1",
    "firedAt": "2026-09-16T12:00:00+00:00",
    "resolvedAt": null,
    "url": "https://argus.example.com/hosts/01a0aa6d-864f-7c6e-8840-23474b485dd1"
  }
}
```

Reports arrive as `report.daily` or `report.weekly` with the summary under `report`, and tests as
`test`. Argus treats any `2xx` answer as delivered and tries other answers again later.

## Live updates

The web app listens on a SignalR hub at `/hubs/live`, with the same cookie session. It calls these
methods on the client, for your own hosts (every host, for administrators):

| Method | When |
| --- | --- |
| `HostMetrics` | A host reported. Carries `hostId` and its `latest` readings. |
| `HostStatus` | A host went offline or came back. Carries `hostId`, `status` and `lastSeenAt`. |
| `AlertChanged` | An alert fired or resolved. Carries `kind`, `alertId`, `hostId`, `title`, `severity`, `value` and `at`. |

## Health checks

`GET /health/live` answers `200` while the server runs. `GET /health/ready` also checks the
database, and is what the container health check uses. Neither needs a sign-in.

## Agent API

Agents use their own endpoints under `/api/agent/v1`, authenticated with the key they receive when
they register (`Authorization: Bearer argus_ak_…`), not with a session.

| Method and path | What it does |
| --- | --- |
| `POST /api/agent/v1/register` | Exchanges an enrollment token (`argus_et_…`) and a description of the machine for a host id and key. |
| `POST /api/agent/v1/metrics` | Sends a batch of up to 500 readings. The answer carries the settings the agent should use. |
| `GET /api/agent/v1/commands` | Waits up to 20 seconds for commands for the agent: container start, stop, restart and logs. Only agents that allow container actions ask. `204` when none came. |
| `POST /api/agent/v1/commands/results` | The agent's answer to a command. |
| `PUT /api/agent/v1/inventory` | Updates what the machine is. |
| `GET /api/agent/v1/update/offer` | The update the agent was asked to install: its `version`, `sha256` and `size`. `204` when there is none. Metrics responses carry the same offer as `update`. |
| `GET /api/agent/v1/update/download` | The offered build. |
| `POST /api/agent/v1/update/result` | Reports a failed update, with the `error`. |

Agent builds and install scripts are served, without a sign-in, under `/downloads`.
