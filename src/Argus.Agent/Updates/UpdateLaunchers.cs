using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Hosting.Systemd;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Argus.Agent.Updates;

/// <summary>Starts installing an update, from inside the running agent.</summary>
internal interface IUpdateLauncher
{
    /// <summary>Hands the update over to the updater. Returns why it cannot, or null.</summary>
    string? Launch(AgentUpdateOffer offer);
}

/// <summary>Starts and stops the agent's service, so the updater can swap its program.</summary>
internal interface IServiceControl
{
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Starts the service, restarting it if it runs.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    Task<bool> IsRunningAsync(CancellationToken cancellationToken);
}

internal static class UpdateLaunchers
{
    public static IUpdateLauncher Create(AgentConfig config)
    {
        if (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService())
        {
            return new WindowsUpdateLauncher(config);
        }

        if (OperatingSystem.IsLinux() && SystemdHelpers.IsSystemdService())
        {
            return new SystemdUpdateLauncher(AgentPaths.LinuxUpdaterUnit, AgentPaths.LinuxUpdateRequestFile);
        }

        return new NoUpdateLauncher();
    }
}

internal sealed class NoUpdateLauncher : IUpdateLauncher
{
    public string? Launch(AgentUpdateOffer offer) =>
        "The agent is not running as an installed service, so it cannot update itself.";
}

/// <summary>
/// The agent runs sandboxed as an unprivileged user and cannot replace itself. It leaves a request file
/// for a root systemd unit, which fetches the update from the server itself, so the agent's own account
/// can never choose what gets installed.
/// </summary>
internal sealed class SystemdUpdateLauncher(string updaterUnit, string requestFile) : IUpdateLauncher
{
    public string? Launch(AgentUpdateOffer offer)
    {
        if (!File.Exists(updaterUnit))
        {
            return "This agent was installed without its updater. Run the install command on the machine once more to add it.";
        }

        try
        {
            File.WriteAllText(requestFile, offer.Version);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The update could not be requested: {ex.Message}";
        }
    }
}

/// <summary>
/// The service runs as SYSTEM but cannot replace its own program while it runs, so it starts a copy of
/// itself that stops the service, swaps the program and starts it again.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsUpdateLauncher(AgentConfig config) : IUpdateLauncher
{
    public string? Launch(AgentUpdateOffer offer)
    {
        if (Environment.ProcessPath is not { } program)
        {
            return "The agent cannot tell where it is installed.";
        }

        try
        {
            // The data folder is open only to SYSTEM and Administrators.
            var updater = Path.Combine(config.ResolvedStateDirectory, "argus-agent-updater.exe");
            File.Copy(program, updater, overwrite: true);

            var start = new ProcessStartInfo(updater) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("apply-update");
            start.ArgumentList.Add("--target");
            start.ArgumentList.Add(program);
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            return $"The updater could not be started: {ex.Message}";
        }
    }
}

internal sealed class SystemdServiceControl(string unit) : IServiceControl
{
    public const string AgentUnit = "argus-agent.service";

    // Linux lets a running program's file be replaced, so the restart alone swaps the program.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var (exitCode, output) = await SystemctlAsync("restart", cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"systemctl restart {unit} failed: {output}");
        }
    }

    public async Task<bool> IsRunningAsync(CancellationToken cancellationToken) =>
        (await SystemctlAsync("is-active", cancellationToken)).ExitCode == 0;

    private async Task<(int ExitCode, string Output)> SystemctlAsync(string command, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("systemctl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Never wait for a password: the updater runs unattended.
        start.ArgumentList.Add("--no-ask-password");
        start.ArgumentList.Add(command);
        start.ArgumentList.Add(unit);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("systemctl could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, ((await output) + (await error)).Trim());
    }
}

[SupportedOSPlatform("windows")]
internal sealed class WindowsServiceControl(string name) : IServiceControl
{
    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(1);

    public Task StopAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var service = new ServiceController(name);
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, Wait);
        }
    }, cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var service = new ServiceController(name);
        if (service.Status != ServiceControllerStatus.Running)
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, Wait);
        }
    }, cancellationToken);

    public Task<bool> IsRunningAsync(CancellationToken cancellationToken) => Task.Run(() =>
    {
        using var service = new ServiceController(name);
        return service.Status == ServiceControllerStatus.Running;
    }, cancellationToken);
}
