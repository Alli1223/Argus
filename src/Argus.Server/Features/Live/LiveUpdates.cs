using Microsoft.AspNetCore.SignalR;

namespace Argus.Server.Features.Live;

/// <summary>
/// Pushes updates to the browsers of a host's owner and of administrators. Live updates are best
/// effort: failing to push must never fail the request or job that caused the update.
/// </summary>
public sealed class LiveUpdates(IHubContext<LiveHub, ILiveClient> hub, ILogger<LiveUpdates> logger)
{
    public Task HostMetricsAsync(Guid ownerId, LiveHostMetrics update) =>
        SendAsync(ownerId, clients => clients.HostMetrics(update));

    public Task HostStatusAsync(Guid ownerId, LiveHostStatus update) =>
        SendAsync(ownerId, clients => clients.HostStatus(update));

    public Task AlertChangedAsync(Guid ownerId, LiveAlert update) =>
        SendAsync(ownerId, clients => clients.AlertChanged(update));

    private async Task SendAsync(Guid ownerId, Func<ILiveClient, Task> send)
    {
        try
        {
            await send(hub.Clients.Groups(LiveGroups.User(ownerId), LiveGroups.Admins));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not push a live update");
        }
    }
}
