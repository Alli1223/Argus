using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Live;

/// <summary>
/// Notices hosts going offline (they simply stop reporting, so nothing else would announce it) and
/// coming back, and pushes the change to their owners' browsers.
/// </summary>
internal sealed class HostStatusMonitor(
    IServiceScopeFactory scopes,
    LiveUpdates live,
    IOptions<AgentOptions> agents,
    TimeProvider time,
    ILogger<HostStatusMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);

    private readonly Dictionary<Guid, HostStatus> _known = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Checking host status failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Compares every host's status with the previous check and announces the changes.</summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var hosts = await scope.ServiceProvider.GetRequiredService<ArgusDbContext>().Hosts
                .AsNoTracking()
                .Select(host => new { host.Id, host.OwnerId, host.LastSeenAt })
                .ToListAsync(cancellationToken);

            var now = time.GetUtcNow();
            foreach (var host in hosts)
            {
                var status = HostAccess.StatusOf(host.LastSeenAt, now, agents.Value);
                if (_known.TryGetValue(host.Id, out var previous) && previous != status)
                {
                    await live.HostStatusAsync(host.OwnerId, new LiveHostStatus(host.Id, status, host.LastSeenAt));
                }

                _known[host.Id] = status;
            }

            // Forget deleted hosts.
            var current = hosts.Select(host => host.Id).ToHashSet();
            foreach (var removed in _known.Keys.Where(id => !current.Contains(id)).ToList())
            {
                _known.Remove(removed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }
}
