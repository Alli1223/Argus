using Argus.Server.Features.Auth;
using Microsoft.AspNetCore.SignalR;

namespace Argus.Server.Features.Live;

/// <summary>Messages the server pushes to browsers.</summary>
public interface ILiveClient
{
    Task HostMetrics(LiveHostMetrics update);

    Task HostStatus(LiveHostStatus update);

    Task AlertChanged(LiveAlert update);
}

internal static class LiveGroups
{
    public const string Admins = "admins";

    public static string User(Guid userId) => $"user:{userId}";
}

/// <summary>
/// Browsers connect here with their session cookie to hear about the hosts they can see.
/// Clients only listen; the hub has no methods for them to call.
/// </summary>
public sealed class LiveHub : Hub<ILiveClient>
{
    public override async Task OnConnectedAsync()
    {
        var user = Context.User!;

        // Administrators see every host, so they join the group that hears everything (and only that
        // group, so no message reaches them twice); everyone else hears about their own hosts.
        var group = user.IsAdmin() ? LiveGroups.Admins : LiveGroups.User(user.GetUserId());
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await base.OnConnectedAsync();
    }
}
