# Configuration reference

## How the server reads settings

The server takes its settings from [`appsettings.json`](../src/Argus.Server/appsettings.json) and
then from environment variables, which win. An environment variable is named after the setting's
key with `__` between the parts, so `Argus:Retention:RawDays` becomes `Argus__Retention__RawDays`
and the first entry of a list, `Argus:Proxy:TrustedProxies`, is `Argus__Proxy__TrustedProxies__0`.

With Docker Compose, set them under the server's `environment:` in
`deploy/docker-compose.override.yml`. A few common ones already have a variable in `deploy/.env`
(see [`.env.example`](../deploy/.env.example)).

The server checks every setting when it starts. A value out of range stops it with a message that
names the setting.

## Server

### General

| Setting | Default | What it does |
| --- | --- | --- |
| `ConnectionStrings:Argus` | none | The PostgreSQL (TimescaleDB) connection string. Required. |
| `Argus:PublicUrl` | none | The address people and agents use to reach Argus. It goes into install commands and into links in emails and chat messages. Without it, install commands use the page's own address and notifications carry no links. |
| `Argus:WebRoot` | `wwwroot` | The folder with the built web app. Without one, as in development, only the API is served. |
| `Argus:Downloads:AgentDirectory` | `agent-dist` | The folder with agent builds, one sub-folder per runtime (`linux-x64`, `linux-arm64`, `win-x64`). |

### Accounts

| Setting | Default | What it does |
| --- | --- | --- |
| `Argus:Auth:AllowRegistration` | `false` | Lets anyone who can reach Argus create a (non-admin) account from the sign-in page. The first account is always set up in the browser. |

### Database

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Database:MigrateOnStartup` | `true` | | Brings the database schema up to date when the server starts. |
| `Argus:Database:StartupRetries` | `30` | 1–1000 | How many times to try reaching the database at startup, 2 seconds apart. |

### Agents

These are sent to every agent, which picks them up with its next report.

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Agents:CollectionIntervalSeconds` | `15` | 5–3600 | How often agents take a reading. |
| `Argus:Agents:InventoryIntervalMinutes` | `60` | 5–1440 | How often agents send what the machine is: its processor, memory, addresses and operating system. |
| `Argus:Agents:TopProcessCount` | `10` | 0–50 | How many of the busiest processes agents report. |
| `Argus:Agents:OfflineAfterSeconds` | `90` | 15–86400 | How long a host can stay silent before it counts as offline. |

### Incoming readings

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Ingest:MaxSampleAgeHours` | `72` | 1–336 | The oldest reading accepted, for agents catching up after an outage. |
| `Argus:Ingest:MaxClockSkewSeconds` | `300` | 0–3600 | How far ahead of the server's clock a reading may be. Readings outside either limit are dropped and logged. |

### Keeping history

Retention changes apply the next time the server starts. See
[Keeping history](deployment.md#keeping-history) for how charts use each kind.

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Retention:RawDays` | `14` | 7–3650 | How long individual readings are kept. |
| `Argus:Retention:FiveMinuteDays` | `90` | 7–3650 | How long five-minute averages are kept. Anomaly rules learn from the past week of these. |
| `Argus:Retention:HourlyDays` | `730` | 30–36500 | How long hourly averages are kept. Reports use these. |

### Alerts

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Alerts:EvaluationIntervalSeconds` | `30` | 5–3600 | How often alert rules are checked. |

### Notifications

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Notifications:PollIntervalSeconds` | `15` | 1–3600 | How often the queue is checked for notifications due another try. New ones go out straight away. |
| `Argus:Notifications:MaxAttempts` | `6` | 1–20 | How many times a notification is tried before Argus gives up. Tries wait 1, 5 and 15 minutes, then 1 hour, then 4 hours. |
| `Argus:Notifications:KeepDays` | `30` | 1–3650 | How long sent and failed notifications are remembered. |

### Email

Email notification channels need a mail server. Without `Host`, email channels can be set up but
cannot send, and the web app says so.

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Smtp:Host` | none | | The mail server. |
| `Argus:Smtp:Port` | `587` | 1–65535 | Its port. |
| `Argus:Smtp:Security` | `Auto` | `Auto`, `None`, `StartTls`, `SslOnConnect` | `Auto` uses TLS on port 465 and STARTTLS elsewhere when the server offers it. |
| `Argus:Smtp:Username`, `Argus:Smtp:Password` | none | | The sign-in, if the mail server needs one. |
| `Argus:Smtp:From` | none | an email address | The address emails come from. Required with a host. |
| `Argus:Smtp:FromName` | `Argus` | | The name emails come from. |

### Reports

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Reports:SendHourUtc` | `7` | 0–23 | The hour (UTC) reports go out. Each covers the day or week up to that hour. |
| `Argus:Reports:WeeklyDay` | `Monday` | `Sunday`–`Saturday` | The day weekly reports go out. |

### Updates

The server looks for new releases on GitHub, tells administrators when one is out, and fetches new
agent builds for agents someone asked to update. Builds are only used when they match the release's
`SHA256SUMS`.

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `Argus:Updates:CheckForUpdates` | `true` | | Look for new releases. Switched off, there are no update notices and agents cannot be updated from Argus. |
| `Argus:Updates:CheckIntervalHours` | `6` | 1–168 | How often to look. |
| `Argus:Updates:Repository` | `Alli1223/Argus` | owner/name | The GitHub repository releases come from, for forks. |
| `Argus:Updates:ApiUrl` | `https://api.github.com` | a URL | GitHub's API. |
| `Argus:Updates:CacheDirectory` | a temporary folder | | Where fetched agent builds are kept. |
| `Argus:Updates:ServerUpdatesDirectory` | none | | The directory shared with the updater service, which installs server releases from **Settings**. The Compose file sets it to `/updates`. Without it, releases are installed by hand. |

### Rate limits

Requests over a limit are refused with `429 Too Many Requests` and a `Retry-After` header.

| Setting | Default | Allowed | What it limits |
| --- | --- | --- | --- |
| `Argus:RateLimits:AuthPermitsPerMinute` | `10` | 1–100000 | Sign-ins, account setup, registration and password changes, per address. |
| `Argus:RateLimits:AgentRegisterPermitsPerMinute` | `60` | 1–100000 | Agent registrations, per address. A fleet behind one NAT address shares it. |
| `Argus:RateLimits:AgentIngestPermitsPerMinute` | `120` | 1–100000 | Readings batches, per agent. |
| `Argus:RateLimits:NotificationTestPermitsPerMinute` | `5` | 1–100000 | Test notifications, per person. |

### Reverse proxies

Argus only believes `X-Forwarded-For` and `X-Forwarded-Proto` from the proxies listed here. See
[Behind your own reverse proxy](deployment.md#behind-your-own-reverse-proxy).

| Setting | Default | What it does |
| --- | --- | --- |
| `Argus:Proxy:TrustedProxies` | none | Addresses of individual proxies, such as `10.0.0.2`. |
| `Argus:Proxy:TrustedNetworks` | none | Networks proxies connect from, in CIDR notation, such as `172.30.0.0/24`. |

## Agent

The install scripts write the agent's settings for you. To change them afterwards, edit the file
and restart the agent (`systemctl restart argus-agent`, or restart the Argus Agent service on
Windows).

The agent reads `/etc/argus-agent/agent.json` on Linux and `C:\ProgramData\Argus\Agent\agent.json`
on Windows, or the file named by the `ARGUS_CONFIG` environment variable. Environment variables
starting with `ARGUS_` override the file: `ARGUS_SERVERURL`, `ARGUS_ENROLLMENTTOKEN`, and so on.

```json
{
  "ServerUrl": "https://argus.example.com",
  "EnrollmentToken": "argus_et_…"
}
```

| Setting | Default | Allowed | What it does |
| --- | --- | --- | --- |
| `ServerUrl` | none | an http or https address | The Argus server. Required. The agent warns when it would send its key over plain HTTP to another machine. |
| `EnrollmentToken` | none | | The token from **Add a system**. Only needed until the agent has registered. |
| `StateDirectory` | `/var/lib/argus-agent`, or `C:\ProgramData\Argus\Agent` | | Where the agent keeps its host id and key. |
| `CollectionIntervalSeconds` | from the server | 5–3600 | Takes readings on this machine at a different pace from the server's setting. |
| `BufferCapacity` | `2880` | 10–100000 | How many readings the agent holds while the server is unreachable: 12 hours at the default pace. |
| `DockerSocket` | `/var/run/docker.sock` | a path | Docker's socket. On Linux the agent lists containers whenever the socket exists and it may use it; see [Containers](#containers). |
| `DriveTemperatures` | `false` | `true` or `false` | Also reads the temperatures of SATA drives on Linux (the `drivetemp` driver). Off by default: on some drives, reading the temperature resets the spin-down timer, so drives meant to sleep would stay awake. NVMe drives are always read. |

### Containers

On Linux, the agent lists Docker's containers, with their state, health and restarts and what each
running one uses, once it may use Docker's socket. The socket belongs to the `docker` group, and
membership lets a program do anything root could, so agents do not join it unless asked: install
with `--docker` (the web app's install command has a box for it), or run the install command again
with it. That adds a systemd drop-in, `/etc/systemd/system/argus-agent.service.d/docker.conf`, with
`SupplementaryGroups=docker`; delete it and restart the agent to stop again.

If Docker's socket is somewhere else, point `DockerSocket` at it. Containers on Windows are not
watched yet.

