using System.ComponentModel;
using System.Diagnostics;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Linux;

/// <summary>Failed systemd service units, as <c>systemctl list-units --state=failed</c> reports them.</summary>
internal sealed class SystemdServices(ILogger<SystemdServices> logger) : IServiceStatusSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private bool _warned;

    /// <summary>True when the machine was booted with systemd.</summary>
    public static bool IsAvailable => Directory.Exists("/run/systemd/system");

    public IReadOnlyList<ServiceProblem>? Collect()
    {
        var start = new ProcessStartInfo("systemctl")
        {
            ArgumentList = { "list-units", "--type=service", "--state=failed", "--no-legend", "--plain", "--no-pager" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment = { ["LANG"] = "C", ["SYSTEMD_COLORS"] = "0" },
        };

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                Warn("systemctl did not answer within {Timeout}", Timeout);
                return null;
            }

            return ParseFailedUnits(output.GetAwaiter().GetResult());
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            Warn("Could not run systemctl to check services: {Reason}", exception.Message);
            return null;
        }
    }

    /// <summary>Parses <c>--plain --no-legend</c> output: unit, load, active and sub state, then a description.</summary>
    internal static List<ServiceProblem> ParseFailedUnits(string output)
    {
        var problems = new List<ServiceProblem>();
        foreach (var raw in output.Split('\n'))
        {
            // Older systemd versions mark failed units with a dot even in plain output.
            var line = raw.Trim().TrimStart('●', '*').Trim();
            var parts = line.Split(' ', 5, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            problems.Add(new ServiceProblem
            {
                Name = parts[0],
                State = parts[2],
                Description = parts.Length > 4 ? parts[4].Trim() : null,
            });
        }

        return problems;
    }

    // Logged once: a machine where systemctl fails will keep failing, and this runs every minute.
    private void Warn(string message, object reason)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
#pragma warning disable CA2254 // The two messages above are fixed templates.
        logger.LogWarning(message, reason);
#pragma warning restore CA2254
    }
}
