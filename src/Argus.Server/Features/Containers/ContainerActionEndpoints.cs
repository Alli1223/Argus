using System.Security.Claims;
using Argus.Contracts.Agent;
using Argus.Server.Data;
using Argus.Server.Features.Agents;
using Argus.Server.Features.Hosts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Containers;

/// <summary>
/// Reading containers' logs and starting, stopping and restarting them, through their host's agent. Open to
/// whoever can see the host, and only on machines whose agent configuration allows container actions.
/// </summary>
public static class ContainerActionEndpoints
{
    /// <summary>Stopping gives the container Docker's 10 seconds, so actions wait longer than that for their answer.</summary>
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan LogsTimeout = TimeSpan.FromSeconds(20);

    private const int MaxResultBytes = 8 * 1024 * 1024;

    public static IEndpointRouteBuilder MapContainerActionEndpoints(this IEndpointRouteBuilder routes)
    {
        var container = routes.MapGroup("/hosts/{id:guid}/containers/{name}").WithTags("Containers");
        container.MapPost("/start", (Guid id, string name, ClaimsPrincipal user, ArgusDbContext db, ContainerStore store, AgentCommandBroker broker, TimeProvider time, CancellationToken cancellationToken) =>
            ActAsync(AgentCommandKind.ContainerStart, id, name, user, db, store, broker, time, cancellationToken));
        container.MapPost("/stop", (Guid id, string name, ClaimsPrincipal user, ArgusDbContext db, ContainerStore store, AgentCommandBroker broker, TimeProvider time, CancellationToken cancellationToken) =>
            ActAsync(AgentCommandKind.ContainerStop, id, name, user, db, store, broker, time, cancellationToken));
        container.MapPost("/restart", (Guid id, string name, ClaimsPrincipal user, ArgusDbContext db, ContainerStore store, AgentCommandBroker broker, TimeProvider time, CancellationToken cancellationToken) =>
            ActAsync(AgentCommandKind.ContainerRestart, id, name, user, db, store, broker, time, cancellationToken));
        container.MapGet("/logs", LogsAsync);
        return routes;
    }

    /// <summary>What agents use to take commands and answer them.</summary>
    public static IEndpointRouteBuilder MapAgentCommandEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet(AgentApi.Commands, async Task<Results<Ok<AgentCommandBatch>, NoContent>> (
                ClaimsPrincipal principal, AgentCommandBroker broker, CancellationToken cancellationToken) =>
            await broker.WaitForCommandsAsync(principal.GetHostId(), cancellationToken) is { Count: > 0 } commands
                ? TypedResults.Ok(new AgentCommandBatch { Commands = commands })
                : TypedResults.NoContent())
            .WithTags("Agent")
            .RequireAuthorization(AgentKeyDefaults.Policy);

        routes.MapPost(AgentApi.CommandResults, (AgentCommandResult result, ClaimsPrincipal principal, AgentCommandBroker broker) =>
            {
                broker.Complete(principal.GetHostId(), result);
                return TypedResults.NoContent();
            })
            .WithTags("Agent")
            .RequireAuthorization(AgentKeyDefaults.Policy)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(MaxResultBytes));

        return routes;
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ActAsync(
        AgentCommandKind kind,
        Guid id,
        string name,
        ClaimsPrincipal user,
        ArgusDbContext db,
        ContainerStore store,
        AgentCommandBroker broker,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        switch (await CheckAsync(id, name, user, db, store, broker, cancellationToken))
        {
            case (false, _):
                return TypedResults.NotFound();
            case (_, { } problem):
                return problem;
        }

        var requestKind = kind switch
        {
            AgentCommandKind.ContainerStart => ContainerEventKinds.StartRequested,
            AgentCommandKind.ContainerStop => ContainerEventKinds.StopRequested,
            _ => ContainerEventKinds.RestartRequested,
        };
        await store.RecordRequestAsync(id, name, requestKind, user.Identity?.Name, time.GetUtcNow(), cancellationToken);

        var command = new AgentCommand { Id = Guid.NewGuid().ToString(), Kind = kind, Container = name };
        return await broker.SendAsync(id, command, ActionTimeout, cancellationToken) switch
        {
            null => NoAnswer(),
            { Succeeded: false } failed => Failed(failed),
            _ => TypedResults.NoContent(),
        };
    }

    private static async Task<Results<Ok<ContainerLogs>, NotFound, ProblemHttpResult>> LogsAsync(
        Guid id,
        string name,
        int? tail,
        ClaimsPrincipal user,
        ArgusDbContext db,
        ContainerStore store,
        AgentCommandBroker broker,
        CancellationToken cancellationToken)
    {
        switch (await CheckAsync(id, name, user, db, store, broker, cancellationToken))
        {
            case (false, _):
                return TypedResults.NotFound();
            case (_, { } problem):
                return problem;
        }

        var command = new AgentCommand
        {
            Id = Guid.NewGuid().ToString(),
            Kind = AgentCommandKind.ContainerLogs,
            Container = name,
            Tail = Math.Clamp(tail ?? 200, 1, AgentLimits.MaxLogLines),
        };
        return await broker.SendAsync(id, command, LogsTimeout, cancellationToken) switch
        {
            null => NoAnswer(),
            { Succeeded: false } failed => Failed(failed),
            var logs => TypedResults.Ok(new ContainerLogs(logs.Logs ?? [], logs.Truncated)),
        };
    }

    /// <summary>Whether the user can see the container, and why the request cannot go to its agent (null when it can).</summary>
    private static async Task<(bool Found, ProblemHttpResult? Problem)> CheckAsync(
        Guid id, string name, ClaimsPrincipal user, ArgusDbContext db, ContainerStore store, AgentCommandBroker broker, CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return (false, null);
        }

        var (exists, actionsEnabled) = await store.GetActionTargetAsync(id, name, cancellationToken);
        if (!exists)
        {
            return (false, null);
        }

        if (!actionsEnabled)
        {
            return (true, TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Container actions are off on this machine",
                detail: "Logs and starting, stopping and restarting containers have to be allowed on the machine itself: " +
                    "run the agent's install command again with --container-actions."));
        }

        if (!broker.IsListening(id))
        {
            return (true, TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The agent is not listening",
                detail: "The machine's agent is not connected for container actions right now. It may be offline or restarting."));
        }

        return (true, null);
    }

    private static ProblemHttpResult NoAnswer() => TypedResults.Problem(
        statusCode: StatusCodes.Status504GatewayTimeout,
        title: "The agent did not answer in time",
        detail: "It may still carry out what was asked. Check the container again in a moment.");

    private static ProblemHttpResult Failed(AgentCommandResult result) => TypedResults.Problem(
        statusCode: StatusCodes.Status502BadGateway,
        title: "Docker refused",
        detail: result.Error ?? "The agent could not do it.");
}
