using System.Security.Claims;
using Argus.Server.Data;
using Argus.Server.Features.Hosts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Proxmox;

/// <summary>
/// Hosts' Proxmox VE virtual machines and containers, and starting, shutting down, restarting and stopping
/// them. Open to whoever can see the host; actions only on machines whose agent configuration allows them.
/// </summary>
public static class ProxmoxEndpoints
{
    public static IEndpointRouteBuilder MapProxmoxEndpoints(this IEndpointRouteBuilder routes)
    {
        var proxmox = routes.MapGroup("/hosts/{id:guid}/proxmox").WithTags("Proxmox");
        proxmox.MapGet("/", GetAsync);
        proxmox.MapPost("/guests/{guestId:int}/{action}", ActAsync);
        return routes;
    }

    private static async Task<Results<Ok<HostProxmox>, NotFound>> GetAsync(
        Guid id, ClaimsPrincipal user, ArgusDbContext db, ProxmoxService proxmox, CancellationToken cancellationToken)
    {
        if (!await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(await proxmox.GetAsync(id, cancellationToken));
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> ActAsync(
        Guid id, int guestId, string action, ClaimsPrincipal user, ArgusDbContext db, ProxmoxService proxmox, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<GuestAction>(action, ignoreCase: true, out var guestAction) || !Enum.IsDefined(guestAction)
            || !await db.Hosts.VisibleTo(user).AnyAsync(host => host.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        return await proxmox.ActAsync(id, guestId, guestAction, user.Identity?.Name, cancellationToken) switch
        {
            null => TypedResults.NoContent(),
            (GuestProblem.NotFound, _) => TypedResults.NotFound(),
            (GuestProblem.NotAllowed, var detail) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Proxmox VE actions are off on this machine", detail: detail),
            (GuestProblem.NotListening, var detail) => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "The agent is not listening", detail: detail),
            (GuestProblem.NoAnswer, var detail) => TypedResults.Problem(statusCode: StatusCodes.Status504GatewayTimeout, title: "The agent did not answer in time", detail: detail),
            (_, var detail) => TypedResults.Problem(statusCode: StatusCodes.Status502BadGateway, title: "Proxmox VE refused", detail: detail),
        };
    }
}
