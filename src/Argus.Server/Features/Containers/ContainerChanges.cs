using Argus.Contracts.Agent;

namespace Argus.Server.Features.Containers;

/// <summary>A container as stored: what the agent last reported, and since when its state has been as it is.</summary>
public sealed record StoredContainer(ContainerInfo Info, DateTimeOffset StateSince);

/// <summary>Something that happened to a container, noticed by comparing two reports.</summary>
public sealed record ContainerEvent(DateTimeOffset Time, string Container, string Kind, string? Detail, int Count = 1);

/// <summary>The kinds of <see cref="ContainerEvent"/>.</summary>
public static class ContainerEventKinds
{
    public const string Appeared = "appeared";
    public const string Removed = "removed";
    public const string Recreated = "recreated";
    public const string Started = "started";
    public const string Stopped = "stopped";
    public const string Restarting = "restarting";
    public const string Restarted = "restarted";
    public const string Paused = "paused";
    public const string Died = "died";
    public const string Unhealthy = "unhealthy";
    public const string Healthy = "healthy";
}

/// <summary>What a new report changes: the containers to store, those that are gone, and the events in between.</summary>
public sealed record ContainerChangeSet(
    IReadOnlyList<StoredContainer> Current, IReadOnlyList<string> Removed, IReadOnlyList<ContainerEvent> Events);

public static class ContainerChanges
{
    /// <summary>
    /// Compares a host's stored containers with its new report. The first report of a host describes how
    /// things are rather than what changed, so it records no events.
    /// </summary>
    public static ContainerChangeSet Compare(
        IReadOnlyCollection<StoredContainer> previous, IReadOnlyList<ContainerInfo> reported, DateTimeOffset checkedAt, bool firstReport)
    {
        var before = previous.ToDictionary(container => container.Info.Name, StringComparer.Ordinal);
        var current = new List<StoredContainer>(reported.Count);
        var events = new List<ContainerEvent>();

        foreach (var info in reported)
        {
            if (!before.Remove(info.Name, out var old))
            {
                current.Add(new StoredContainer(info, checkedAt));
                if (!firstReport)
                {
                    events.Add(new ContainerEvent(checkedAt, info.Name, ContainerEventKinds.Appeared, info.Image));
                }

                continue;
            }

            var changed = old.Info.State != info.State || old.Info.Health != info.Health;
            current.Add(new StoredContainer(info, changed ? checkedAt : old.StateSince));
            events.AddRange(Describe(old.Info, info, checkedAt));
        }

        events.AddRange(before.Keys.Select(name => new ContainerEvent(checkedAt, name, ContainerEventKinds.Removed, null)));
        return new ContainerChangeSet(current, [.. before.Keys], events);
    }

    private static IEnumerable<ContainerEvent> Describe(ContainerInfo old, ContainerInfo now, DateTimeOffset time)
    {
        if (old.Id != now.Id)
        {
            yield return new ContainerEvent(time, now.Name, ContainerEventKinds.Recreated, now.Image);
        }
        else if (now.RestartCount > old.RestartCount)
        {
            yield return new ContainerEvent(time, now.Name, ContainerEventKinds.Restarted, null, now.RestartCount - old.RestartCount);
        }

        if (old.State != now.State && StateEvent(now) is { } stateEvent)
        {
            yield return stateEvent with { Time = time };
        }

        if (old.Health != now.Health && now.State == "running")
        {
            if (now.Health == "unhealthy")
            {
                yield return new ContainerEvent(time, now.Name, ContainerEventKinds.Unhealthy, null);
            }
            else if (now.Health == "healthy" && old.Health == "unhealthy")
            {
                yield return new ContainerEvent(time, now.Name, ContainerEventKinds.Healthy, null);
            }
        }
    }

    private static ContainerEvent? StateEvent(ContainerInfo container) => container.State switch
    {
        "running" => new ContainerEvent(default, container.Name, ContainerEventKinds.Started, null),
        "restarting" => new ContainerEvent(default, container.Name, ContainerEventKinds.Restarting, null),
        "paused" => new ContainerEvent(default, container.Name, ContainerEventKinds.Paused, null),
        "exited" => new ContainerEvent(default, container.Name, ContainerEventKinds.Stopped, ExitDetail(container)),
        "dead" => new ContainerEvent(default, container.Name, ContainerEventKinds.Died, ExitDetail(container)),
        _ => null,
    };

    private static string? ExitDetail(ContainerInfo container) => (container.ExitCode, container.OomKilled) switch
    {
        (_, true) => "out of memory",
        ({ } code, _) => $"exit code {code}",
        _ => null,
    };
}
