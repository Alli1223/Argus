using Argus.Contracts.Agent;
using Argus.Server.Features.Containers;

namespace Argus.Server.Tests.Containers;

public class ContainerChangesTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 9, 17, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = Earlier.AddMinutes(1);

    internal static ContainerInfo Container(string name, string state = "running", string? health = null, int restarts = 0, string id = "a1") => new()
    {
        Id = id,
        Name = name,
        Image = "nginx:1.29",
        State = state,
        Health = health,
        RestartCount = restarts,
        ExitCode = state == "exited" ? 1 : null,
        CreatedAt = Earlier.AddDays(-1),
    };

    private static List<StoredContainer> Stored(params ContainerInfo[] containers) =>
        containers.Select(container => new StoredContainer(container, Earlier)).ToList();

    private static string[] Kinds(ContainerChangeSet changes) =>
        changes.Events.Select(change => $"{change.Container}:{change.Kind}").ToArray();

    [Fact]
    public void A_first_report_describes_how_things_are_without_events()
    {
        var changes = ContainerChanges.Compare([], [Container("web"), Container("db", "exited")], Now, firstReport: true);

        Assert.Empty(changes.Events);
        Assert.All(changes.Current, stored => Assert.Equal(Now, stored.StateSince));
    }

    [Fact]
    public void Containers_that_come_and_go_are_noted()
    {
        var changes = ContainerChanges.Compare(Stored(Container("web"), Container("old")), [Container("web"), Container("new")], Now, firstReport: false);

        Assert.Equal(["new:appeared", "old:removed"], Kinds(changes));
        Assert.Equal(["old"], changes.Removed);
    }

    [Fact]
    public void State_health_restarts_and_recreation_become_events()
    {
        var changes = ContainerChanges.Compare(
            Stored(Container("web", restarts: 1), Container("api", health: "healthy"), Container("job"), Container("app", id: "a1")),
            [
                Container("web", restarts: 4),
                Container("api", health: "unhealthy"),
                Container("job", "exited"),
                Container("app", id: "b2"),
            ],
            Now,
            firstReport: false);

        Assert.Equal(["web:restarted", "api:unhealthy", "job:stopped", "app:recreated"], Kinds(changes));
        Assert.Equal(3, changes.Events[0].Count);
        Assert.Equal("exit code 1", changes.Events[2].Detail);
    }

    [Fact]
    public void State_since_moves_only_when_state_or_health_changes()
    {
        var changes = ContainerChanges.Compare(
            Stored(Container("web", restarts: 1), Container("api", health: "starting")),
            [Container("web", restarts: 2), Container("api", health: "healthy")],
            Now,
            firstReport: false);

        Assert.Equal(Earlier, changes.Current.Single(stored => stored.Info.Name == "web").StateSince);
        Assert.Equal(Now, changes.Current.Single(stored => stored.Info.Name == "api").StateSince);
        // Healthy after starting is how a container comes up, not a recovery worth noting.
        Assert.Equal(["web:restarted"], Kinds(changes));
    }
}
