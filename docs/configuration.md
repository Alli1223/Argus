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

Email notification channels need a mail server. Without one, email channels can be set up but cannot
send, and the web app says so.

Administrators set the mail server under **Settings**, which is the simplest way and needs no access
to the machine Argus runs on: the settings are kept in the database, the password encrypted with this
server's data protection keys, and **Send test** says what the mail server answered. Settings saved
there are used in place of the ones below, which stay as they are in the file until someone forgets
the saved ones again.

The settings below are the other way to do it, for servers set up from files alone.

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
| `ContainerActions` | `false` | `true` or `false` | Lets people who can see this machine in Argus read its containers' logs and start, stop and restart them; see [Containers](#containers). |
| `PowerActions` | `false` | `true` or `false` | Lets people who can see this machine in Argus, and their alert rules, shut it down and restart it; see [Power](#power). |
| `DockerSocket` | `/var/run/docker.sock` | a path | Docker's socket. On Linux the agent lists containers whenever the socket exists and it may use it; see [Containers](#containers). |
| `HostRoot` | none | a path | Where the machine's own filesystem is mounted when the agent runs in a container, such as `/host`; see [The agent in a container](#the-agent-in-a-container). |
| `UpsServer` | `localhost:3493` | `host` or `host:port`, or empty | The Network UPS Tools server (upsd) to read UPSes from; see [UPS](#ups). Empty stops the agent looking. |
| `DriveTemperatures` | `false` | `true` or `false` | Also reads the temperatures of SATA drives (the `drivetemp` driver on Linux, the storage drivers on Windows). Off by default: on some drives, reading the temperature resets the spin-down timer, so drives meant to sleep would stay awake. NVMe drives are always read. |

### The agent in a container

Some machines cannot run the agent as a service: NAS boxes and appliances often have no systemd, and
some have no package manager either. They do run Docker, so the agent has an image of its own,
`ghcr.io/alli1223/argus-agent`, which watches the machine from a container.

```sh
docker run -d --name argus-agent --restart always \
  --network host --pid host \
  --mount type=bind,source=/,target=/host,readonly,bind-propagation=rslave \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v argus-agent:/var/lib/argus-agent \
  -e ARGUS_SERVERURL=https://argus.example.com \
  -e ARGUS_ENROLLMENTTOKEN=argus_et_… \
  -e ARGUS_CONTAINERACTIONS=true \
  ghcr.io/alli1223/argus-agent:latest
```

Each part earns its place:

- `--mount … target=/host,readonly` is the machine itself, which `HostRoot` (set to `/host` in the
  image) tells the agent to read: its `/proc`, its `/sys`, its `/etc` and the free space on its disks,
  rather than the container's. `rslave` carries mounts made later, such as a disk plugged in, into the
  container. Nothing is written through it.
- `--pid host` lets the agent see the machine's processes; without it, the busiest processes are the
  container's own.
- `--network host` gives the machine's addresses and its own network counters.
- The Docker socket is what lists containers, and `ARGUS_CONTAINERACTIONS=true` allows their logs and
  start, stop and restart. Leave both out to watch the machine only.
- `-e ARGUS_POWERACTIONS=true`, which is not in the example, would let Argus shut the machine down and
  restart it; see [Power](#power).
- The named volume keeps the agent's identity, so a restarted container is the same host in Argus and
  not a new one.

The agent runs as root in the container, because reading another machine's `/proc` and using Docker's
socket are root's to do. It cannot update itself there: the image is the version, so upgrade it as any
other container, with `docker pull` and a fresh `docker run`, or through whatever the NAS offers.
Services are not watched, as a machine without systemd has none to watch.

### UPS

The agent reads uninterruptible power supplies from Network UPS Tools (NUT), which most NAS systems
run when a UPS is plugged in by USB, and which Linux machines get from the `nut` package. It asks the
machine's own UPS server, `upsd`, on port 3493 every reading: each UPS's state (on line power, on
battery, low battery, battery to replace), its charge, the runtime left, its load and its voltages.
Reading them needs no login. Where no `upsd` answers the agent tries again a minute later, so machines
without a UPS cost nothing. The agent in a container reaches the machine's `upsd` through
`--network host`.

To read a UPS server elsewhere, such as the NAS a UPS is plugged into from another machine, set
`UpsServer` to its address; the NAS must allow that machine to connect (on Synology, **Permitted
DiskStation devices** under **UPS**).

Every account starts with three UPS rules, which stay quiet on hosts without a UPS: a warning when a
UPS runs on its battery, a warning when its charge stays below 75% for a minute and a critical alert
below 40%. Rules on the runtime left and on the load can be added under **Rules**.

To shut machines down when a UPS runs low, add a rule on **UPS charge on battery** (or **UPS runtime
on battery**), such as below 40%, and choose the machines under **Shut down when it fires**. These two
count only readings taken on battery, so a battery that is still low while it charges after an outage
shuts nothing down, and their alert resolves as soon as line power is back. With **Wake them when it
resolves**, Argus then wakes the same machines with Wake-on-LAN. The machines' agents must allow power
actions; see [Power](#power). The NAS the UPS is plugged into usually shuts itself down through NUT
when the battery is nearly empty, so set the rule's threshold above that point.

### Power

Shutting a machine down and restarting it from Argus is off unless the machine itself allows it, as
with container actions. Install with `--power-actions` (`-PowerActions` on Windows, or
`-e ARGUS_POWERACTIONS=true` for the agent's container), or run the install command again with it.
Whoever can see the machine in Argus can then shut it down or restart it from its page, and alert
rules can shut it down; each time is listed in the machine's **Power** section.

- On Linux the agent runs unprivileged, so `--power-actions` adds two root systemd units,
  `argus-agent-power.path` and `argus-agent-power.service`. The agent writes `poweroff` or `reboot`
  to `/var/lib/argus-agent/power-requested`, and the units run `systemctl` for it. Requests older than
  two minutes are ignored.
- On Windows the service runs `shutdown.exe`, which gives five seconds' notice.
- The agent's container asks the machine's init system directly, which needs `--pid host`: systemd
  and BusyBox init by signal, SysV init through `/run/initctl`. On machines with AppArmor, Docker's
  profile stops containers signalling the machine; add `--security-opt apparmor=unconfined` there.

Waking a machine needs its network card to have Wake-on-LAN switched on, in the BIOS and in the
operating system. Agents report each network card's hardware address, so Argus knows it once the
machine has reported. The Argus server usually sits behind Docker's own network, where broadcasts do
not reach the machines, so the packet is sent by the agents of your other machines instead: up to
three that are connected, preferring those on the same network. Every agent from 0.11 does this; it
needs no setting, as the packet can only ask a sleeping machine to start. A machine's **Wake-on-LAN
settings** can give another hardware address, or an address to send the packet to, such as the
network's broadcast address.

### Containers

On Linux, the agent lists Docker's containers, with their state, health and restarts and what each
running one uses, once it may use Docker's socket. The socket belongs to the `docker` group, and
membership lets a program do anything root could, so agents do not join it unless asked: install
with `--docker` (the web app's install command has a box for it), or run the install command again
with it. That adds a systemd drop-in, `/etc/systemd/system/argus-agent.service.d/docker.conf`, with
`SupplementaryGroups=docker`; delete it and restart the agent to stop again.

If Docker's socket is somewhere else, point `DockerSocket` at it. Containers on Windows are not
watched yet.

Reading containers' logs and starting, stopping and restarting them from Argus is off unless the
machine itself allows it, so that an Argus login alone never controls a machine's containers. Install
with `--container-actions` (which includes `--docker`), or run the install command again with it; it
adds `/etc/systemd/system/argus-agent.service.d/container-actions.conf`, which sets
`ARGUS_CONTAINERACTIONS=true`. The agent, which keeps a request open with the server for commands,
then carries out these for containers: start, stop (Docker gives the container 10 seconds) and restart a container by
name, and read its newest log lines. Logs pass through the server without being stored. Whoever asked for
a start, stop or restart is noted in the container's events.

