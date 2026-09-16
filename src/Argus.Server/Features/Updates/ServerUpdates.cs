using System.Text.Json;
using Argus.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Updates;

public sealed record ServerUpdateLogEntry(DateTimeOffset At, string Message);

/// <summary>
/// One update as the updater records it. <see cref="State"/> moves through starting, downloading,
/// backing-up, deploying, verifying and perhaps rolling-back, and ends as succeeded, rolled-back or failed.
/// </summary>
public sealed record ServerUpdateRun(
    string Id,
    string From,
    string To,
    string? RequestedBy,
    string State,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string? Error,
    string? Backup,
    string? ServerLog,
    IReadOnlyList<ServerUpdateLogEntry>? Log)
{
    public bool InProgress => State is not ("succeeded" or "rolled-back" or "failed");
}

/// <summary>
/// Whether this server can install releases itself, or why not; an update asked for that the updater has
/// not started yet; and the latest update it ran.
/// </summary>
public sealed record ServerSelfUpdate(
    bool Available,
    string? Unavailable,
    string? UpdaterVersion,
    string? PendingVersion,
    ServerUpdateRun? LastRun);

public sealed record ServerUpdateRequest(string Version);

/// <summary>
/// Talks to the updater service through the directory the two share: the updater writes a heartbeat
/// (updater.json) and the progress of its latest update (status.json), and this writes the request
/// (request.json) naming the release to install. The updater checks the request again itself.
/// </summary>
public sealed class ServerUpdates(IOptions<UpdateOptions> options, UpdateStatus releases, TimeProvider time, ILogger<ServerUpdates> logger)
{
    /// <summary>An idle updater writes a heartbeat every few seconds.</summary>
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Mid-update the updater can be quiet for a while, such as when Docker downloads an image.</summary>
    private static readonly TimeSpan BusyHeartbeatTimeout = TimeSpan.FromMinutes(30);

    private sealed record Heartbeat(string? UpdaterVersion, DateTimeOffset HeartbeatAt, string? Problem);

    private sealed record UpdateRequest(string Id, string Version, string? RequestedBy, DateTimeOffset RequestedAt);

    public ServerSelfUpdate Describe()
    {
        var directory = options.Value.ServerUpdatesDirectory;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new ServerSelfUpdate(false, "This server was not installed with the updater, so releases are installed by hand.", null, null, null);
        }

        var run = Read<ServerUpdateRun>(directory, "status.json");
        var heartbeat = Read<Heartbeat>(directory, "updater.json");
        var request = Read<UpdateRequest>(directory, "request.json");
        var pending = request is not null && request.Id != run?.Id ? request.Version : null;

        string? unavailable = null;
        if (!options.Value.CheckForUpdates)
        {
            unavailable = "Update checks are switched off on this server.";
        }
        else if (heartbeat is null)
        {
            unavailable = "The updater is not running.";
        }
        else if (time.GetUtcNow() - heartbeat.HeartbeatAt > (run?.InProgress == true ? BusyHeartbeatTimeout : HeartbeatTimeout))
        {
            unavailable = $"The updater has not been heard from since {heartbeat.HeartbeatAt:u}.";
        }
        else if (!string.IsNullOrWhiteSpace(heartbeat.Problem))
        {
            unavailable = heartbeat.Problem;
        }

        return new ServerSelfUpdate(unavailable is null, unavailable, heartbeat?.UpdaterVersion, pending, run);
    }

    /// <summary>Asks the updater to install <paramref name="version"/>; returns why not when it cannot.</summary>
    public string? Request(string version, string? requestedBy)
    {
        var state = Describe();
        if (!state.Available)
        {
            return state.Unavailable;
        }

        if (state.LastRun is { InProgress: true } running)
        {
            return $"An update to {running.To} is already running.";
        }

        if (state.PendingVersion is { } pending)
        {
            return $"An update to {pending} is already waiting to start.";
        }

        var latest = releases.Current.Latest;
        if (latest is null)
        {
            return "No release is known yet. Check for updates first.";
        }

        if (latest.Version != version)
        {
            return $"Only the latest release, {latest.Version}, can be installed.";
        }

        if (!ReleaseVersions.IsNewer(version, ServerVersion.Current))
        {
            return $"This server already runs {ServerVersion.Current}.";
        }

        var request = new UpdateRequest(Guid.NewGuid().ToString(), version, requestedBy, time.GetUtcNow());
        var path = Path.Combine(options.Value.ServerUpdatesDirectory!, "request.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(request, JsonSerializerOptions.Web));
        File.Move(temporary, path, overwrite: true);

        logger.LogInformation("{User} asked to update Argus from {Current} to {Version}", requestedBy, ServerVersion.Current, version);
        return null;
    }

    private T? Read<T>(string directory, string name)
        where T : class
    {
        var path = Path.Combine(directory, name);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonSerializerOptions.Web) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning("Could not read {File} from the updater: {Reason}", path, ex.Message);
            return null;
        }
    }
}
