using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Argus.Server.Features.Notifications;

/// <summary>Wakes the dispatcher when notifications are queued, so they go out without waiting for the next poll.</summary>
public sealed class NotificationSignal
{
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _wake.Writer.TryWrite(true);

    /// <summary>Waits until <see cref="Notify"/> is called or the timeout passes.</summary>
    public async Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await _wake.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }
}

/// <summary>Runs the <see cref="NotificationDispatcher"/> whenever notifications are queued, and on a poll for retries.</summary>
internal sealed class NotificationDispatchService(
    IServiceScopeFactory scopes,
    NotificationSignal signal,
    IOptions<NotificationOptions> options,
    TimeProvider time,
    ILogger<NotificationDispatchService> logger) : BackgroundService
{
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundDelivery)
        {
            logger.LogInformation("Background notification delivery is switched off");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(options.Value.PollIntervalSeconds);
        var lastPruned = DateTimeOffset.MinValue;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<NotificationDispatcher>();

                    // A full batch may mean more are due, so keep going until the queue is drained.
                    DispatchResult result;
                    do
                    {
                        result = await dispatcher.DispatchDueAsync(stoppingToken);
                    }
                    while (result.Total == NotificationDispatcher.BatchSize);

                    if (time.GetUtcNow() - lastPruned > PruneInterval)
                    {
                        await dispatcher.PruneAsync(stoppingToken);
                        lastPruned = time.GetUtcNow();
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Sending notifications failed");
                }

                await signal.WaitAsync(pollInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
