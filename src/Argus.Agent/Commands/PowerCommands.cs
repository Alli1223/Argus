using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;
using Microsoft.Extensions.Hosting.Systemd;

namespace Argus.Agent.Commands;

internal enum PowerAction
{
    PowerOff,
    Reboot,
}

/// <summary>Powers the machine off or restarts it, in whatever way this agent is able to.</summary>
internal interface IPowerControl
{
    /// <summary>Starts it. Returns why it could not, or null once the machine has been asked.</summary>
    Task<string?> RequestAsync(PowerAction action, string reason, CancellationToken cancellationToken);
}

/// <summary>
/// Carries out shutdown and restart commands from the server, only on machines whose agent configuration
/// sets PowerActions. The answer goes back as soon as the machine has been asked, before it goes down.
/// </summary>
internal sealed class PowerCommands(AgentConfig config, IPowerControl control, ILogger<PowerCommands> logger)
{
    public async Task<AgentCommandResult> RunAsync(AgentCommand command, CancellationToken cancellationToken)
    {
        if (!config.PowerActions)
        {
            return Failed(command, "Shutting down and restarting are off on this machine.");
        }

        var action = command.Kind == AgentCommandKind.Restart ? PowerAction.Reboot : PowerAction.PowerOff;
        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Asked through Argus" : command.Reason.Trim();
        logger.LogWarning("{Action} the machine, as asked through Argus: {Reason}",
            action == PowerAction.Reboot ? "Restarting" : "Shutting down", reason);

        var error = await control.RequestAsync(action, reason, cancellationToken);
        if (error is not null)
        {
            logger.LogWarning("Could not {Action} the machine: {Error}", action == PowerAction.Reboot ? "restart" : "shut down", error);
            return Failed(command, error);
        }

        return new AgentCommandResult { Id = command.Id, Succeeded = true };
    }

    private static AgentCommandResult Failed(AgentCommand command, string error) =>
        new() { Id = command.Id, Succeeded = false, Error = error };
}

internal static class PowerControls
{
    public static IPowerControl Create(AgentConfig config)
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPowerControl();
        }

        if (OperatingSystem.IsLinux() && !string.IsNullOrWhiteSpace(config.HostRoot))
        {
            return new HostInitPowerControl(config.HostRoot);
        }

        if (OperatingSystem.IsLinux() && SystemdHelpers.IsSystemdService())
        {
            return new SystemdPowerRequest(AgentPaths.LinuxPowerUnit, AgentPaths.LinuxPowerRequestFile);
        }

        return new NoPowerControl();
    }
}

internal sealed class NoPowerControl : IPowerControl
{
    public Task<string?> RequestAsync(PowerAction action, string reason, CancellationToken cancellationToken) =>
        Task.FromResult<string?>("The agent is not running as an installed service or in its container, so it cannot do that.");
}

/// <summary>
/// The installed Linux agent runs sandboxed as an unprivileged user, so it leaves a request file for a
/// root systemd unit, which install.sh adds with --power-actions, to power off or reboot the machine.
/// </summary>
internal sealed class SystemdPowerRequest(string powerUnit, string requestFile) : IPowerControl
{
    public Task<string?> RequestAsync(PowerAction action, string reason, CancellationToken cancellationToken)
    {
        if (!File.Exists(powerUnit))
        {
            return Task.FromResult<string?>(
                "This agent was installed without power actions. Run its install command on the machine again with --power-actions.");
        }

        try
        {
            File.WriteAllText(requestFile, PowerRequests.Format(action));
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult<string?>($"The request could not be written: {ex.Message}");
        }
    }
}

/// <summary>What the request file says, and how old a request may be before it no longer counts.</summary>
internal static class PowerRequests
{
    /// <summary>A request older than this is left over from before a restart, and is ignored.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(2);

    public static string Format(PowerAction action) => action == PowerAction.Reboot ? "reboot" : "poweroff";

    public static PowerAction? Parse(string text) => text.Trim() switch
    {
        "poweroff" => PowerAction.PowerOff,
        "reboot" => PowerAction.Reboot,
        _ => null,
    };

    /// <summary>The 384-byte request SysV init reads from /run/initctl: change to runlevel 0 or 6.</summary>
    public static byte[] Initctl(PowerAction action)
    {
        var request = new byte[384];
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(0), 0x03091969);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(request.AsSpan(8), action == PowerAction.Reboot ? '6' : '0');
        return request;
    }
}

/// <summary>
/// In its container the agent cannot run the machine's own programs, but it shares the machine's processes
/// (<c>--pid host</c>) and sees its files under /host. It asks the machine's init system directly, the way
/// the shutdown programs do: systemd and BusyBox init by signal, SysV init through its /run/initctl pipe.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed partial class HostInitPowerControl(string hostRoot) : IPowerControl
{
    // glibc's SIGRTMIN is 34, which systemd uses: SIGRTMIN+4 is poweroff.target, SIGRTMIN+5 reboot.target.
    private const int SystemdPowerOff = 34 + 4;
    private const int SystemdReboot = 34 + 5;

    // BusyBox init: SIGUSR2 powers off, SIGTERM reboots.
    private const int BusyBoxPowerOff = 12;
    private const int BusyBoxReboot = 15;

    private const int Eperm = 1;
    private const int Esrch = 3;

    public async Task<string?> RequestAsync(PowerAction action, string reason, CancellationToken cancellationToken)
    {
        // Without --pid host the agent is process 1 of its own container, and would only stop itself.
        if (Environment.ProcessId == 1)
        {
            return "The agent's container does not share the machine's processes. Run it with --pid host.";
        }

        var init = ReadOrEmpty("/proc/1/comm").Trim();
        if (init == "systemd")
        {
            return Signal(action == PowerAction.Reboot ? SystemdReboot : SystemdPowerOff);
        }

        var initctl = Path.Combine(hostRoot, "run/initctl");
        if (File.Exists(initctl))
        {
            return await InitctlAsync(initctl, action, cancellationToken);
        }

        if (init == "init" && IsBusyBox())
        {
            return Signal(action == PowerAction.Reboot ? BusyBoxReboot : BusyBoxPowerOff);
        }

        return $"The machine's init system ({(init.Length > 0 ? init : "unknown")}) is not one the agent knows how to ask from its container. " +
            "Install the agent as a service instead.";
    }

    private static string? Signal(int signal)
    {
        if (Kill(1, signal) == 0)
        {
            return null;
        }

        return Marshal.GetLastPInvokeError() switch
        {
            Eperm => "The machine refused the request. Run the agent's container as root, and on machines with AppArmor " +
                "add --security-opt apparmor=unconfined.",
            Esrch => "The machine's init process could not be found.",
            var errno => $"The machine refused the request (error {errno}).",
        };
    }

    private static async Task<string?> InitctlAsync(string path, PowerAction action, CancellationToken cancellationToken)
    {
        try
        {
            // Opening a pipe waits for its reader, which init always is; give up rather than wait for ever.
            await Task.Run(() =>
            {
                using var pipe = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                pipe.Write(PowerRequests.Initctl(action));
            }, cancellationToken).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            return null;
        }
        catch (TimeoutException)
        {
            return "The machine's init did not read the request.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"The machine's init did not take the request: {ex.Message}";
        }
    }

    private bool IsBusyBox()
    {
        try
        {
            return File.ResolveLinkTarget(Path.Combine(hostRoot, "sbin/init"), returnFinalTarget: false) is { } target
                && target.ToString().Contains("busybox", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string ReadOrEmpty(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);
}

/// <summary>The service runs as SYSTEM, which may shut Windows down with its own shutdown program.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPowerControl : IPowerControl
{
    public async Task<string?> RequestAsync(PowerAction action, string reason, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(action == PowerAction.Reboot ? "/r" : "/s");

        // A few seconds' grace lets the agent answer the server first.
        start.ArgumentList.Add("/t");
        start.ArgumentList.Add("5");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add(reason.Length > 500 ? reason[..500] : reason);

        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("shutdown.exe could not be started.");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 ? null : $"shutdown.exe failed: {((await output) + (await error)).Trim()}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return ex.Message;
        }
    }
}
