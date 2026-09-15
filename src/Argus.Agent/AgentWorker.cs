using Argus.Agent.Collection;
using Argus.Agent.Configuration;
using Argus.Agent.State;
using Argus.Agent.Transport;
using Argus.Contracts.Agent;

namespace Argus.Agent;

/// <summary>
/// The agent's main loop: make sure the agent is registered, then on every tick collect a sample,
/// send whatever is buffered (backing off while the server is unavailable) and refresh the
/// inventory now and then. Collection never stops while sending fails; the buffer absorbs outages.
/// </summary>
internal sealed class AgentWorker(
    AgentConfig config,
    StateStore stateStore,
    SampleCollector collector,
    ISystemInfoSource systemInfo,
    Registrar registrar,
    ArgusClient client,
    SampleBuffer buffer,
    TimeProvider time,
    ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly TimeSpan FinalFlushTimeout = TimeSpan.FromSeconds(3);

    private readonly Backoff _sendBackoff = new(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
    private AgentSettings _settings = new();
    private AgentState? _state;
    private DateTimeOffset _nextSendAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextInventoryAt = DateTimeOffset.MinValue;
    private long _reportedDrops;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Argus agent {Version} starting; reporting to {Server}", AgentInfo.Version, config.ServerUrl);
        if (config.UsesPlainHttpToRemoteServer)
        {
            logger.LogWarning("The server URL uses plain http, so the agent key travels unencrypted. Use https.");
        }

        collector.Prime();

        _state = await EnsureRegisteredAsync(stoppingToken);
        if (_state is null)
        {
            return;
        }

        var interval = CurrentInterval();
        using var timer = new PeriodicTimer(interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Collect();
                await FlushAsync(ignoreBackoff: false, stoppingToken);
                await RefreshInventoryAsync(stoppingToken);

                var next = CurrentInterval();
                if (next != interval)
                {
                    interval = next;
                    timer.Period = next;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        // One brief, last attempt to deliver what is still buffered.
        if (_state is not null && buffer.Count > 0)
        {
            using var timeout = new CancellationTokenSource(FinalFlushTimeout);
            await FlushAsync(ignoreBackoff: true, timeout.Token);
        }
    }

    private TimeSpan CurrentInterval() =>
        TimeSpan.FromSeconds(config.CollectionIntervalSeconds ?? Math.Clamp(_settings.CollectionIntervalSeconds, 5, 3600));

    private void Collect()
    {
        try
        {
            collector.TopProcessCount = _settings.TopProcessCount;
            buffer.Add(collector.Collect());

            if (buffer.Dropped > _reportedDrops)
            {
                logger.LogWarning("The sample buffer is full ({Capacity} samples); dropped {Count} of the oldest",
                    buffer.Capacity, buffer.Dropped - _reportedDrops);
                _reportedDrops = buffer.Dropped;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Collecting metrics failed");
        }
    }

    private async Task FlushAsync(bool ignoreBackoff, CancellationToken cancellationToken)
    {
        if (!ignoreBackoff && time.GetUtcNow() < _nextSendAt)
        {
            return;
        }

        while (buffer.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var batch = buffer.PeekBatch(AgentLimits.MaxSamplesPerBatch);
            var result = await client.SendMetricsAsync(_state!.AgentKey, new MetricsBatch { Samples = batch }, cancellationToken);

            if (result.IsSuccess)
            {
                buffer.Remove(batch.Count);
                if (_sendBackoff.Failures > 0)
                {
                    logger.LogInformation("Connection to the server restored");
                }

                _sendBackoff.Reset();
                ApplySettings(result.Value!.Settings);
                continue;
            }

            switch (result.Failure)
            {
                case FailureKind.Rejected:
                    // Resending a batch the server refuses would block everything behind it.
                    logger.LogWarning("The server rejected {Count} sample(s) ({Reason}); discarding them", batch.Count, result.Describe());
                    buffer.Remove(batch.Count);
                    continue;

                case FailureKind.Unauthorized:
                    logger.LogError("The server no longer accepts this agent's key ({Reason})", result.Describe());
                    if (await TryReRegisterAsync(cancellationToken))
                    {
                        continue;
                    }

                    break;
            }

            var delay = result.RetryAfter ?? _sendBackoff.NextDelay();
            _nextSendAt = time.GetUtcNow() + delay;
            logger.LogWarning("Could not send metrics ({Reason}); {Buffered} sample(s) buffered, retrying in {Delay:g}",
                result.Describe(), buffer.Count, delay);
            return;
        }
    }

    /// <summary>After the key stops working (host deleted, or registered again elsewhere), try the enrollment token once more.</summary>
    private async Task<bool> TryReRegisterAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.EnrollmentToken))
        {
            logger.LogError("No enrollment token is configured, so the agent cannot register again. " +
                "Create a token in the web UI and run 'argus-agent register'.");
            return false;
        }

        var result = await registrar.RegisterAsync(config.EnrollmentToken, cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogError("Registering again failed: {Reason}", result.Describe());
            return false;
        }

        _state = result.Value;
        return true;
    }

    private async Task<AgentState?> EnsureRegisteredAsync(CancellationToken cancellationToken)
    {
        var backoff = new Backoff(TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5));
        while (!cancellationToken.IsCancellationRequested)
        {
            var saved = stateStore.Load();
            if (saved is not null && SameServer(saved.ServerUrl, config.ServerUrl))
            {
                logger.LogInformation("Reporting as host {HostId}", saved.HostId);

                // Send inventory on the first tick: the agent or the machine may have changed since it last ran.
                _nextInventoryAt = DateTimeOffset.MinValue;
                return saved;
            }

            TimeSpan delay;
            if (string.IsNullOrWhiteSpace(config.EnrollmentToken))
            {
                logger.LogError("The agent is not registered and no enrollment token is configured. " +
                    "Add EnrollmentToken to the config file or run 'argus-agent register --token <token>'.");
                delay = TimeSpan.FromMinutes(1);
            }
            else
            {
                var result = await registrar.RegisterAsync(config.EnrollmentToken, cancellationToken);
                if (result.IsSuccess)
                {
                    // Registration already carried the inventory.
                    _nextInventoryAt = time.GetUtcNow().AddMinutes(_settings.InventoryIntervalMinutes);
                    return result.Value;
                }

                // A bad token will not fix itself quickly; anything else is worth retrying sooner.
                delay = result.Failure == FailureKind.Unauthorized ? TimeSpan.FromMinutes(5) : result.RetryAfter ?? backoff.NextDelay();
                logger.LogError("Registration failed ({Reason}); retrying in {Delay:g}", result.Describe(), delay);
            }

            try
            {
                await Task.Delay(delay, time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    private async Task RefreshInventoryAsync(CancellationToken cancellationToken)
    {
        if (time.GetUtcNow() < _nextInventoryAt)
        {
            return;
        }

        var report = new InventoryReport { AgentVersion = AgentInfo.Version, SystemInfo = systemInfo.Collect() };
        var result = await client.SendInventoryAsync(_state!.AgentKey, report, cancellationToken);
        if (result.IsSuccess)
        {
            ApplySettings(result.Value);
        }
        else
        {
            logger.LogWarning("Could not send the system inventory: {Reason}", result.Describe());
        }

        _nextInventoryAt = time.GetUtcNow().AddMinutes(result.IsSuccess ? _settings.InventoryIntervalMinutes : 5);
    }

    private void ApplySettings(AgentSettings? settings)
    {
        if (settings is null || settings == _settings)
        {
            return;
        }

        _settings = settings;
        logger.LogInformation(
            "Server settings: collect every {Interval} s, report the top {TopProcesses} processes, refresh inventory every {Inventory} min",
            settings.CollectionIntervalSeconds, settings.TopProcessCount, settings.InventoryIntervalMinutes);
    }

    private static bool SameServer(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
