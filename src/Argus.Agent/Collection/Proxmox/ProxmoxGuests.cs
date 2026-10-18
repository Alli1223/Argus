using System.Text.Json;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Proxmox;

/// <summary>This machine's Proxmox VE node: its API, and its name there.</summary>
internal sealed class ProxmoxNode(ProxmoxClient client) : IDisposable
{
    private string? _name;

    public ProxmoxClient Client => client;

    public static ProxmoxNode? Create(AgentConfig config) =>
        config.ReadsProxmox ? new ProxmoxNode(new ProxmoxClient(config.ProxmoxUrl, config.ProxmoxToken!, config.ProxmoxFingerprint)) : null;

    /// <summary>
    /// The node's name in Proxmox VE. In a cluster each node lists only its own guests, so an agent on every
    /// node reports each guest once. Without the privilege to read the cluster's status, the machine's name
    /// is the node's: Proxmox VE names nodes after their hostname.
    /// </summary>
    public async Task<string> NameAsync(CancellationToken cancellationToken)
    {
        if (_name is not null)
        {
            return _name;
        }

        try
        {
            _name = ProxmoxParsers.LocalNode(await client.GetAsync("cluster/status", cancellationToken));
        }
        catch (ProxmoxException ex) when (ex.Status == System.Net.HttpStatusCode.Forbidden)
        {
        }

        _name ??= Environment.MachineName.Split('.')[0];
        return _name;
    }

    public void Dispose() => client.Dispose();
}

internal interface IProxmoxSource
{
    /// <summary>The node's guests when a report is due, else null.</summary>
    ProxmoxReport? Collect();
}

/// <summary>For machines that are not Proxmox VE nodes, or whose agent has no token for it.</summary>
internal sealed class NoProxmox : IProxmoxSource
{
    public ProxmoxReport? Collect() => null;
}

/// <summary>
/// Lists the node's virtual machines and containers. The list goes to the server when a guest's state
/// changes, and at least once a minute with what the guests use.
/// </summary>
internal sealed class ProxmoxGuests(ProxmoxNode node, bool actionsEnabled, TimeProvider time, ILogger<ProxmoxGuests> logger)
    : IProxmoxSource
{
    public static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(10);

    private string? _lastState;
    private DateTimeOffset _lastReportAt = DateTimeOffset.MinValue;
    private string? _lastProblem;

    public ProxmoxReport? Collect()
    {
        using var timeout = new CancellationTokenSource(CollectTimeout);
        ProxmoxReport report;
        try
        {
            report = CollectAsync(timeout.Token).GetAwaiter().GetResult();
            _lastProblem = null;
        }
        catch (Exception ex) when (ex is ProxmoxException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            var problem = ex is OperationCanceledException ? "Proxmox VE did not answer within 10 seconds." : ex.Message;
            if (problem != _lastProblem)
            {
                logger.LogWarning("Could not read Proxmox VE's guests: {Problem}", problem);
                _lastProblem = problem;
            }

            report = new ProxmoxReport { Problem = problem, ActionsEnabled = actionsEnabled };
        }

        return Due(report) ? report : null;
    }

    private async Task<ProxmoxReport> CollectAsync(CancellationToken cancellationToken)
    {
        var name = await node.NameAsync(cancellationToken);
        var version = await node.Client.GetAsync("version", cancellationToken);
        var path = $"nodes/{Uri.EscapeDataString(name)}";
        var machines = ProxmoxParsers.ParseGuests(await node.Client.GetAsync($"{path}/qemu?full=1", cancellationToken), ProxmoxParsers.VirtualMachine);
        var containers = ProxmoxParsers.ParseGuests(await node.Client.GetAsync($"{path}/lxc", cancellationToken), ProxmoxParsers.Container);

        return new ProxmoxReport
        {
            Node = name,
            Version = version.ValueKind == JsonValueKind.Object && version.TryGetProperty("version", out var number) ? number.GetString() : null,
            ActionsEnabled = actionsEnabled,
            Guests = [.. machines.Concat(containers).OrderBy(guest => guest.Id).Take(AgentLimits.MaxProxmoxGuests)],
        };
    }

    /// <summary>Whether a report should go out now: when a guest's state changed, or a minute after the last.</summary>
    private bool Due(ProxmoxReport report)
    {
        var state = string.Join('\n', [
            report.Node, report.Problem, report.ActionsEnabled.ToString(),
            .. report.Guests.Select(guest => $"{guest.Id} {guest.Type} {guest.Name} {guest.Status} {guest.Lock} {guest.Template} {string.Join(',', guest.Tags)}"),
        ]);
        var now = time.GetUtcNow();
        if (state == _lastState && now - _lastReportAt < ReportInterval)
        {
            return false;
        }

        _lastState = state;
        _lastReportAt = now;
        return true;
    }
}
