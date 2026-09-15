using Argus.Server.Features.Alerts;

namespace Argus.Server.Features.Live;

/// <summary>Forwards alert state changes to the browsers of the people who can see the host.</summary>
internal sealed class LiveAlertEventSink(LiveUpdates live) : IAlertEventSink
{
    public async Task PublishAsync(IReadOnlyList<AlertEvent> events, CancellationToken cancellationToken)
    {
        foreach (var alertEvent in events)
        {
            await live.AlertChangedAsync(alertEvent.OwnerId, new LiveAlert(
                alertEvent.Kind,
                alertEvent.AlertId,
                alertEvent.HostId,
                alertEvent.Title,
                alertEvent.Severity,
                alertEvent.Value,
                alertEvent.At));
        }
    }
}
