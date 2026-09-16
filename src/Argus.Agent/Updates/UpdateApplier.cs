using Argus.Agent.Transport;
using Argus.Contracts.Agent;

namespace Argus.Agent.Updates;

/// <summary>
/// Installs the update the server offers: downloads it next to the installed agent, checks it, swaps it
/// in and restarts the service. When the new agent does not keep running, the old one goes back. Failures
/// are reported to the server; success shows when the new agent reports its version.
/// </summary>
internal sealed class UpdateApplier(ArgusClient client, IServiceControl service, ILogger logger, TimeSpan settleTime)
{
    /// <summary>How long the new agent must keep running before the update counts as done.</summary>
    public static readonly TimeSpan DefaultSettleTime = TimeSpan.FromSeconds(15);

    public async Task<int> ApplyAsync(string agentKey, string program, CancellationToken cancellationToken)
    {
        var offered = await client.GetUpdateOfferAsync(agentKey, cancellationToken);
        if (!offered.IsSuccess)
        {
            logger.LogError("Could not ask the server for the update: {Reason}", offered.Describe());
            return 1;
        }

        if (offered.Value is not { } offer)
        {
            logger.LogInformation("The server has no update for this agent");
            return 0;
        }

        // Next to the installed program: only administrators can write there, so the checked file stays as checked.
        var directory = Path.GetDirectoryName(Path.GetFullPath(program))!;
        var incoming = Path.Combine(directory, $"argus-agent-{offer.Version}.new{Path.GetExtension(program)}");
        var previous = program + ".previous";

        logger.LogInformation("Downloading agent {Version}", offer.Version);
        var download = await client.DownloadUpdateAsync(agentKey, offer, incoming, cancellationToken);
        if (!download.IsSuccess)
        {
            return await FailAsync(agentKey, offer, $"Downloading agent {offer.Version} failed: {download.Describe()}", cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(incoming,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        var reported = await UpdateFiles.VersionOfAsync(incoming, cancellationToken);
        if (reported != offer.Version)
        {
            File.Delete(incoming);
            return await FailAsync(agentKey, offer,
                $"The downloaded agent says it is version {reported ?? "(no answer)"}, not {offer.Version}.", cancellationToken);
        }

        string problem;
        try
        {
            await service.StopAsync(cancellationToken);
            File.Copy(program, previous, overwrite: true);

            // A rename, so the program is never half-written (and Linux lets a running program's file be replaced).
            File.Move(incoming, program, overwrite: true);
            await service.StartAsync(cancellationToken);
            await Task.Delay(settleTime, cancellationToken);
            if (await service.IsRunningAsync(cancellationToken))
            {
                logger.LogInformation("Updated to agent {Version}", offer.Version);
                File.Delete(previous);
                return 0;
            }

            problem = $"Agent {offer.Version} did not keep running, so {AgentInfo.Version} was put back.";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            problem = $"Installing agent {offer.Version} failed ({ex.Message}), so {AgentInfo.Version} was put back.";
        }
        finally
        {
            File.Delete(incoming);
        }

        logger.LogError("{Problem}", problem);
        try
        {
            await service.StopAsync(cancellationToken);
            if (File.Exists(previous))
            {
                File.Move(previous, program, overwrite: true);
            }

            await service.StartAsync(cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            problem = $"{problem[..^1]}, but restarting it failed too. See the updater's log on the machine.";
            logger.LogError(ex, "Putting agent {Version} back failed", AgentInfo.Version);
        }

        return await FailAsync(agentKey, offer, problem, cancellationToken);
    }

    private async Task<int> FailAsync(string agentKey, AgentUpdateOffer offer, string problem, CancellationToken cancellationToken)
    {
        logger.LogError("Update to agent {Version} failed: {Problem}", offer.Version, problem);
        var report = await client.ReportUpdateResultAsync(
            agentKey, new AgentUpdateResult { Version = offer.Version, Succeeded = false, Error = problem }, cancellationToken);
        if (!report.IsSuccess)
        {
            logger.LogWarning("Could not tell the server the update failed: {Reason}", report.Describe());
        }

        return 1;
    }
}
