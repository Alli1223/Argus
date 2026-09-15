using System.ComponentModel;
using System.Runtime.Versioning;
using System.ServiceProcess;
using Argus.Contracts.Agent;
using Microsoft.Win32;

namespace Argus.Agent.Collection.Windows;

/// <summary>
/// Services set to start automatically that are not running. Trigger-started services are left out:
/// Windows starts them on demand and stops them again when idle, so for them being stopped is normal.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsServices(ILogger<WindowsServices> logger) : IServiceStatusSource
{
    public IReadOnlyList<ServiceProblem>? Collect()
    {
        try
        {
            var problems = new List<ServiceProblem>();
            foreach (var service in ServiceController.GetServices())
            {
                using (service)
                {
                    if (service.StartType == ServiceStartMode.Automatic
                        && service.Status == ServiceControllerStatus.Stopped
                        && !IsTriggerStarted(service.ServiceName))
                    {
                        problems.Add(new ServiceProblem
                        {
                            Name = service.ServiceName,
                            Description = service.DisplayName,
                            State = "stopped",
                        });
                    }
                }
            }

            return problems;
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            logger.LogWarning(exception, "Could not list Windows services");
            return null;
        }
    }

    private static bool IsTriggerStarted(string serviceName)
    {
        using var triggers = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}\TriggerInfo");
        return triggers is not null;
    }
}
