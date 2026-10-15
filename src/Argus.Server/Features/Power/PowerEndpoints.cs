using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Containers;
using Argus.Server.Features.Hosts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Power;

/// <summary>
/// Shutting hosts down, restarting them and waking them. Open to whoever can see the host; shutting down and
/// restarting only on machines whose agent configuration allows power actions.
/// </summary>
public static class PowerEndpoints
{
    private const int HistoryCount = 20;

    public static IEndpointRouteBuilder MapPowerEndpoints(this IEndpointRouteBuilder routes)
    {
        var power = routes.MapGroup("/hosts/{id:guid}/power").WithTags("Power");
        power.MapGet("/", GetAsync);
        power.MapPut("/settings", UpdateSettingsAsync);
        power.MapPost("/shutdown", (Guid id, ClaimsPrincipal user, ArgusDbContext db, PowerService power, CancellationToken cancellationToken) =>
            ShutdownAsync(PowerActionKind.Shutdown, id, user, db, power, cancellationToken));
        power.MapPost("/restart", (Guid id, ClaimsPrincipal user, ArgusDbContext db, PowerService power, CancellationToken cancellationToken) =>
            ShutdownAsync(PowerActionKind.Restart, id, user, db, power, cancellationToken));
        power.MapPost("/wake", WakeAsync);
        return routes;
    }

    private static async Task<Results<Ok<HostPower>, NotFound>> GetAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, PowerService power, AgentCommandBroker broker, CancellationToken cancellationToken)
    {
        var host = await db.Hosts.AsNoTracking().VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        return host is null ? TypedResults.NotFound() : TypedResults.Ok(await DescribeAsync(host, power, broker, cancellationToken));
    }

    private static async Task<Results<Ok<HostPower>, NotFound, ValidationProblem>> UpdateSettingsAsync(
        Guid id,
        PowerSettingsRequest request,
        ClaimsPrincipal user,
        ArgusDbContext db,
        PowerService power,
        AgentCommandBroker broker,
        CancellationToken cancellationToken)
    {
        var host = await db.Hosts.VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        var errors = new Dictionary<string, string[]>();
        string? mac = null;
        if (!string.IsNullOrWhiteSpace(request.WakeMacAddress) && (mac = PowerAddresses.NormalizeMac(request.WakeMacAddress)) is null)
        {
            errors["wakeMacAddress"] = ["Give a hardware address such as aa:bb:cc:dd:ee:ff."];
        }

        var address = string.IsNullOrWhiteSpace(request.WakeAddress) ? null : request.WakeAddress.Trim();
        if (address is not null && !PowerAddresses.TryParseNetwork(address, out _, out _))
        {
            errors["wakeAddress"] = ["Give an IPv4 address such as 192.168.1.255, or 192.168.1.20/24."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        host.WakeMacAddress = mac;
        host.WakeAddress = address;
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await DescribeAsync(host, power, broker, cancellationToken));
    }

    private static async Task<Results<Ok<PowerActionInfo>, NotFound, ProblemHttpResult>> ShutdownAsync(
        PowerActionKind kind, Guid id, ClaimsPrincipal user, ArgusDbContext db, PowerService power, CancellationToken cancellationToken)
    {
        var host = await db.Hosts.AsNoTracking().VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        var reason = $"{(kind == PowerActionKind.Restart ? "Restart" : "Shutdown")} asked by {user.Identity?.Name} in Argus";
        return Answer(await power.ShutdownAsync(host, kind, reason, user.Identity?.Name, null, cancellationToken));
    }

    private static async Task<Results<Ok<PowerActionInfo>, NotFound, ProblemHttpResult>> WakeAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, PowerService power, CancellationToken cancellationToken)
    {
        var host = await db.Hosts.AsNoTracking().VisibleTo(user).SingleOrDefaultAsync(h => h.Id == id, cancellationToken);
        if (host is null)
        {
            return TypedResults.NotFound();
        }

        return Answer(await power.WakeAsync(host, $"Wake asked by {user.Identity?.Name} in Argus", user.Identity?.Name, null, cancellationToken));
    }

    private static async Task<HostPower> DescribeAsync(MonitoredHost host, PowerService power, AgentCommandBroker broker, CancellationToken cancellationToken)
    {
        List<string> addresses = [.. host.WakeAddress is { } address ? [address] : Array.Empty<string>(), .. host.Networks];
        var relays = await power.RelaysAsync(host, addresses, cancellationToken);
        return new HostPower(
            host.AgentCommands.Contains(nameof(Contracts.Agent.AgentCommandKind.Shutdown)),
            broker.IsListening(host.Id),
            host.MacAddresses,
            host.Networks,
            host.WakeMacAddress,
            host.WakeAddress,
            relays.Count,
            await power.HistoryAsync(host.Id, HistoryCount, cancellationToken));
    }

    private static Results<Ok<PowerActionInfo>, NotFound, ProblemHttpResult> Answer(PowerOutcome outcome)
    {
        var action = outcome.Action;
        var info = new PowerActionInfo(action.Id, action.Kind, action.RequestedBy, action.RuleId, null, action.Reason, action.Succeeded, action.Detail, action.At);
        return outcome.Problem switch
        {
            null => TypedResults.Ok(info),
            PowerProblem.NoAnswer => TypedResults.Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "The agent did not answer in time", detail: action.Detail),
            PowerProblem.Refused => TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "The agent could not do it", detail: action.Detail),
            PowerProblem.NotAllowed => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Power actions are off on this machine", detail: action.Detail),
            PowerProblem.NotListening => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The agent is not connected", detail: action.Detail),
            PowerProblem.NoAddress => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "No hardware address to wake", detail: action.Detail),
            _ => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Nothing to send the packet", detail: action.Detail),
        };
    }
}
