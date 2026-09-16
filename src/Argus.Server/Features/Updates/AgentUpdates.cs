using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Hosts;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Updates;

/// <summary>
/// Agent updates: which hosts could update, asking them to, and what to offer an agent that was asked.
/// An agent is only offered a build once it has been downloaded and checked.
/// </summary>
public sealed class AgentUpdates(ArgusDbContext db, UpdateStatus status, AgentPackages packages, TimeProvider time)
{
    /// <summary>The release version the host's agent could update to, or null.</summary>
    public static string? AvailableFor(MonitoredHost host, ReleaseInfo? release) =>
        release is not null
        && AgentPackages.RuntimeOf(host.Platform, host.Architecture) is { } runtime
        && AgentPackages.HasBuild(release, runtime)
        && ReleaseVersions.IsNewer(release.Version, host.AgentVersion)
            ? release.Version
            : null;

    /// <summary>
    /// Asks the host's agent to update to the latest release, and starts fetching that build. Returns
    /// why it cannot, or null. The caller saves the change.
    /// </summary>
    public string? Request(MonitoredHost host)
    {
        var release = status.Current.Latest;
        if (AvailableFor(host, release) is not { } version)
        {
            return release is null
                ? "Argus has not found a release to update to."
                : AgentPackages.RuntimeOf(host.Platform, host.Architecture) is null
                    ? $"There are no agent builds for {host.Platform} on {host.Architecture}."
                    : $"The agent already runs {host.AgentVersion}, and the latest release is {release.Version}.";
        }

        host.AgentUpdateVersion = version;
        host.AgentUpdateRequestedAt = time.GetUtcNow();
        host.AgentUpdateError = null;

        // Failures are recorded on the host when its agent next reports.
        _ = packages.PrepareAsync(release!, AgentPackages.RuntimeOf(host.Platform, host.Architecture)!, retryFailed: true);
        return null;
    }

    public static void Cancel(MonitoredHost host)
    {
        host.AgentUpdateVersion = null;
        host.AgentUpdateRequestedAt = null;
        host.AgentUpdateError = null;
    }

    /// <summary>The update the host's agent should install now, if it was asked to and the build is ready.</summary>
    public async Task<AgentUpdateOffer?> OfferAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var (release, runtime) = await PendingAsync(hostId, cancellationToken);
        if (release is null || runtime is null)
        {
            return null;
        }

        var download = packages.PrepareAsync(release, runtime);
        if (download.IsCompletedSuccessfully)
        {
            var package = download.Result;
            return new AgentUpdateOffer { Version = package.Version, Sha256 = package.Sha256, Size = package.Size };
        }

        if (download.IsFaulted)
        {
            await FailAsync(hostId, $"Agent {release.Version} could not be fetched: {download.Exception!.InnerException?.Message}", cancellationToken);
        }

        return null;
    }

    /// <summary>The build the host's agent was offered, if it is ready.</summary>
    public async Task<AgentPackage?> PackageForAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var (release, runtime) = await PendingAsync(hostId, cancellationToken);
        if (release is null || runtime is null)
        {
            return null;
        }

        var download = packages.PrepareAsync(release, runtime);
        return download.IsCompletedSuccessfully ? download.Result : null;
    }

    /// <summary>Records why the update failed and withdraws the request, so the agent is not offered it again.</summary>
    public Task FailAsync(Guid hostId, string error, CancellationToken cancellationToken)
    {
        var message = error.Length > MonitoredHostConfiguration.ErrorLength ? error[..MonitoredHostConfiguration.ErrorLength] : error;
        return db.Hosts
            .Where(host => host.Id == hostId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(host => host.AgentUpdateVersion, (string?)null)
                .SetProperty(host => host.AgentUpdateRequestedAt, (DateTimeOffset?)null)
                .SetProperty(host => host.AgentUpdateError, message), cancellationToken);
    }

    /// <summary>The release and runtime of the host's requested update, while it still applies.</summary>
    private async Task<(ReleaseInfo? Release, string? Runtime)> PendingAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var host = await db.Hosts.AsNoTracking()
            .Where(h => h.Id == hostId && h.AgentUpdateVersion != null)
            .Select(h => new { h.Platform, h.Architecture, h.AgentVersion, h.AgentUpdateVersion })
            .SingleOrDefaultAsync(cancellationToken);
        var release = status.Current.Latest;
        if (host is null || release is null || release.Version != host.AgentUpdateVersion)
        {
            return (null, null);
        }

        var runtime = AgentPackages.RuntimeOf(host.Platform, host.Architecture);
        return runtime is not null && AgentPackages.HasBuild(release, runtime) && ReleaseVersions.IsNewer(release.Version, host.AgentVersion)
            ? (release, runtime)
            : (null, null);
    }
}
