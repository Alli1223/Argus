using System.Globalization;
using System.Text.Json;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Proxmox;

/// <summary>Reads Proxmox VE's answers, kept free of I/O so they are easy to test.</summary>
internal static class ProxmoxParsers
{
    public const string VirtualMachine = "qemu";
    public const string Container = "lxc";

    /// <summary>This node's name, from /cluster/status: the node entry marked local.</summary>
    public static string? LocalNode(JsonElement status)
    {
        if (status.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var entry in status.EnumerateArray())
        {
            if (Text(entry, "type") == "node" && Number(entry, "local") == 1 && Text(entry, "name") is { Length: > 0 } name)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>The guests in a /nodes/{node}/qemu or /nodes/{node}/lxc list.</summary>
    public static List<ProxmoxGuest> ParseGuests(JsonElement list, string type)
    {
        var guests = new List<ProxmoxGuest>();
        if (list.ValueKind != JsonValueKind.Array)
        {
            return guests;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (Number(entry, "vmid") is not { } id || id is < 1 or > int.MaxValue)
            {
                continue;
            }

            var status = Text(entry, "status") ?? "unknown";

            // A paused virtual machine still counts as running; QEMU's own status tells.
            if (type == VirtualMachine && status == "running" && Text(entry, "qmpstatus") is "paused" or "suspended" or "prelaunch")
            {
                status = "paused";
            }

            var running = status == "running";
            guests.Add(new ProxmoxGuest
            {
                Id = (int)id,
                Type = type,
                Name = Text(entry, "name") is { Length: > 0 } name ? name : $"{(type == Container ? "CT" : "VM")} {id}",
                Status = status,
                Lock = Text(entry, "lock") is { Length: > 0 } guestLock ? guestLock : null,
                Template = Number(entry, "template") == 1,
                UptimeSeconds = running ? Number(entry, "uptime") ?? 0 : 0,
                CpuPercent = running && Real(entry, "cpu") is { } cpu ? Math.Round(Math.Clamp(cpu * 100, 0, 100), 2) : null,
                Cpus = Real(entry, "cpus"),
                MemoryBytes = running ? Number(entry, "mem") : null,
                MaxMemoryBytes = Number(entry, "maxmem") is > 0 and var maxMemory ? maxMemory : null,
                MaxDiskBytes = Number(entry, "maxdisk") is > 0 and var maxDisk ? maxDisk : null,
                Tags = (Text(entry, "tags") ?? "")
                    .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Take(AgentLimits.MaxProxmoxTags)
                    .ToList(),
            });
        }

        return guests;
    }

    /// <summary>
    /// How a task (from /nodes/{node}/tasks/{upid}/status) went: null while it runs, "OK" when it succeeded,
    /// otherwise Proxmox VE's reason.
    /// </summary>
    public static string? TaskOutcome(JsonElement status) =>
        Text(status, "status") == "stopped" ? Text(status, "exitstatus") ?? "OK" : null;

    private static string? Text(JsonElement entry, string name) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    /// <summary>A whole number; Proxmox VE sends some as strings (container ids, for one) and some as numbers.</summary>
    private static long? Number(JsonElement entry, string name) =>
        Real(entry, name) is { } value && double.IsFinite(value) && value is >= long.MinValue and <= long.MaxValue ? (long)value : null;

    private static double? Real(JsonElement entry, string name) =>
        Text(entry, name) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}
