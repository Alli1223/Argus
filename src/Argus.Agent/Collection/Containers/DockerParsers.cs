using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Containers;

/// <summary>Cumulative counters of one container, from which the next reading's rates are computed.</summary>
internal readonly record struct ContainerCounters(ulong ContainerCpu, ulong SystemCpu, ulong? RxBytes, ulong? TxBytes);

/// <summary>
/// Reads Docker's JSON. Only what Argus shows is taken: container environments and commands, which
/// often hold secrets, are never read.
/// </summary>
internal static partial class DockerParsers
{
    private const string ComposeProjectLabel = "com.docker.compose.project";
    private const string ComposeServiceLabel = "com.docker.compose.service";

    /// <summary>A container from its entry in the container list and its full inspection.</summary>
    public static ContainerInfo ParseContainer(JsonElement listed, JsonElement inspected)
    {
        var state = inspected.GetProperty("State");
        var status = String(state, "Status") ?? String(listed, "State") ?? "unknown";
        var running = status is "running" or "paused" or "restarting";
        var labels = inspected.TryGetProperty("Config", out var config) && config.TryGetProperty("Labels", out var found)
            && found.ValueKind == JsonValueKind.Object
                ? found
                : default;

        return new ContainerInfo
        {
            Id = String(inspected, "Id") ?? String(listed, "Id") ?? "",
            Name = (String(inspected, "Name") ?? "").TrimStart('/'),
            Image = (config.ValueKind == JsonValueKind.Object ? String(config, "Image") : null) ?? String(listed, "Image") ?? "",
            State = status,
            Health = state.TryGetProperty("Health", out var health) && health.ValueKind == JsonValueKind.Object
                ? String(health, "Status")
                : null,
            RestartCount = inspected.TryGetProperty("RestartCount", out var restarts) && restarts.TryGetInt32(out var count) ? count : 0,
            ExitCode = running || status == "created" ? null : Int(state, "ExitCode"),
            OomKilled = state.TryGetProperty("OOMKilled", out var oom) && oom.ValueKind == JsonValueKind.True,
            CreatedAt = Time(inspected, "Created") ?? DateTimeOffset.UnixEpoch,
            StartedAt = Time(state, "StartedAt"),
            FinishedAt = Time(state, "FinishedAt"),
            RestartPolicy = inspected.TryGetProperty("HostConfig", out var host) && host.TryGetProperty("RestartPolicy", out var policy)
                ? String(policy, "Name") is { Length: > 0 } name ? name : "no"
                : null,
            ComposeProject = labels.ValueKind == JsonValueKind.Object ? String(labels, ComposeProjectLabel) : null,
            ComposeService = labels.ValueKind == JsonValueKind.Object ? String(labels, ComposeServiceLabel) : null,
            Ports = Ports(listed),
        };
    }

    /// <summary>The counters in a one-shot stats reading.</summary>
    public static ContainerCounters ParseCounters(JsonElement stats)
    {
        var cpu = stats.GetProperty("cpu_stats");
        ulong? rx = null, tx = null;
        if (stats.TryGetProperty("networks", out var networks) && networks.ValueKind == JsonValueKind.Object)
        {
            rx = 0;
            tx = 0;
            foreach (var network in networks.EnumerateObject())
            {
                rx += UInt(network.Value, "rx_bytes");
                tx += UInt(network.Value, "tx_bytes");
            }
        }

        return new ContainerCounters(
            cpu.TryGetProperty("cpu_usage", out var usage) ? UInt(usage, "total_usage") : 0,
            UInt(cpu, "system_cpu_usage"),
            rx,
            tx);
    }

    /// <summary>
    /// What a container used between two readings. CPU is its share of all the machine's CPU time, and
    /// memory leaves out inactive file cache, as <c>docker stats</c> does.
    /// </summary>
    public static ContainerUsage Usage(string name, JsonElement stats, ContainerCounters? previous, double seconds)
    {
        var current = ParseCounters(stats);
        double cpu = 0;
        double? rx = null, tx = null;
        if (previous is { } before)
        {
            var system = Counters.Delta(before.SystemCpu, current.SystemCpu);
            cpu = system == 0 ? 0 : Math.Clamp(100.0 * Counters.Delta(before.ContainerCpu, current.ContainerCpu) / system, 0, 100);
            if (seconds > 0 && current.RxBytes is { } rxNow && before.RxBytes is { } rxThen
                && current.TxBytes is { } txNow && before.TxBytes is { } txThen)
            {
                rx = Counters.Rate(rxThen, rxNow, seconds);
                tx = Counters.Rate(txThen, txNow, seconds);
            }
        }

        long memory = 0;
        long? limit = null;
        if (stats.TryGetProperty("memory_stats", out var memoryStats) && memoryStats.ValueKind == JsonValueKind.Object)
        {
            var used = UInt(memoryStats, "usage");
            ulong cache = 0;
            if (memoryStats.TryGetProperty("stats", out var detail) && detail.ValueKind == JsonValueKind.Object)
            {
                // cgroup v2 reports inactive_file; v1 total_inactive_file, or only cache on old kernels.
                cache = UInt(detail, "inactive_file") is > 0 and var inactive ? inactive
                    : UInt(detail, "total_inactive_file") is > 0 and var total ? total
                    : UInt(detail, "cache");
            }

            memory = (long)(used > cache ? used - cache : 0);
            limit = UInt(memoryStats, "limit") is > 0 and var max ? (long)max : null;
        }

        return new ContainerUsage
        {
            Name = name,
            CpuPercent = cpu,
            MemoryBytes = memory,
            MemoryLimitBytes = limit,
            NetRxBytesPerSec = rx,
            NetTxBytesPerSec = tx,
        };
    }

    /// <summary>Published ports from the container list, such as "0.0.0.0:8080->80/tcp", or "80/tcp" when unpublished.</summary>
    private static List<string> Ports(JsonElement listed)
    {
        if (!listed.TryGetProperty("Ports", out var ports) || ports.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return ports.EnumerateArray()
            .Select(port =>
            {
                var inside = $"{Int(port, "PrivatePort")}/{String(port, "Type") ?? "tcp"}";
                return Int(port, "PublicPort") is { } published
                    ? $"{String(port, "IP") ?? "0.0.0.0"}:{published}->{inside}"
                    : inside;
            })
            .Distinct(StringComparer.Ordinal)
            .Take(AgentLimits.MaxContainerPorts)
            .ToList();
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static ulong UInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var number)
            ? number
            : 0;

    /// <summary>
    /// Docker's times, where "0001-01-01T00:00:00Z" means never. They have nanoseconds, two digits more than
    /// .NET keeps, so the extra digits go first.
    /// </summary>
    private static DateTimeOffset? Time(JsonElement element, string name) =>
        String(element, name) is { } text
        && DateTimeOffset.TryParse(
            ExtraFractionDigits().Replace(text, "$1"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
        && time.Year > 1
            ? time.ToUniversalTime()
            : null;

    [GeneratedRegex(@"(\.\d{7})\d+")]
    private static partial Regex ExtraFractionDigits();
}
