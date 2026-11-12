using System.Text.Json;
using Argus.Agent.Configuration;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Unifi;

internal interface IUnifiSource
{
    /// <summary>The network's state; null from agents that do not read UniFi.</summary>
    UnifiReport? Collect();
}

/// <summary>For agents without a UniFi console to read.</summary>
internal sealed class NoUnifi : IUnifiSource
{
    public UnifiReport? Collect() => null;
}

/// <summary>
/// Reads a UniFi site: internet traffic and client counts on every sample, and its devices and what each
/// client moved about once a minute. Client traffic is the change in each client's byte counters since the
/// last report; counters that went backwards (the client reconnected) start afresh.
/// </summary>
internal sealed class UnifiNetwork(UnifiClient client, string site, TimeProvider time, ILogger<UnifiNetwork> logger)
    : IUnifiSource, IDisposable
{
    public static readonly TimeSpan DetailInterval = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(10);

    private readonly Dictionary<string, (long Downloaded, long Uploaded)> _counters = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _countersAt;
    private DateTimeOffset _nextDetail = DateTimeOffset.MinValue;
    private string? _siteName;
    private string? _version;
    private string? _lastProblem;

    public static UnifiNetwork? Create(AgentConfig config, TimeProvider time, ILoggerFactory loggers) =>
        config.ReadsUnifi
            ? new UnifiNetwork(
                new UnifiClient(config.UnifiUrl!, config.UnifiApiKey!, config.UnifiFingerprint),
                config.UnifiSite,
                time,
                loggers.CreateLogger<UnifiNetwork>())
            : null;

    public UnifiReport? Collect()
    {
        using var timeout = new CancellationTokenSource(CollectTimeout);
        try
        {
            var report = CollectAsync(timeout.Token).GetAwaiter().GetResult();
            _lastProblem = null;
            return report;
        }
        catch (Exception ex) when (ex is UnifiException or JsonException or OperationCanceledException or InvalidOperationException)
        {
            var problem = ex is OperationCanceledException ? "The UniFi console did not answer within 10 seconds." : ex.Message;
            if (problem != _lastProblem)
            {
                logger.LogWarning("Could not read the UniFi console: {Problem}", problem);
                _lastProblem = problem;
            }

            return new UnifiReport { Problem = problem };
        }
    }

    private async Task<UnifiReport> CollectAsync(CancellationToken cancellationToken)
    {
        var path = $"api/s/{Uri.EscapeDataString(site)}/";
        var health = UnifiParsers.ParseHealth(await client.GetAsync(path + "stat/health", cancellationToken));

        IReadOnlyList<UnifiDevice>? devices = null;
        IReadOnlyList<UnifiClientUsage>? clients = null;
        var now = time.GetUtcNow();
        if (now >= _nextDetail)
        {
            _nextDetail = now + DetailInterval;
            await DescribeSiteAsync(cancellationToken);
            devices = [.. UnifiParsers.ParseDevices(await client.GetAsync(path + "stat/device", cancellationToken)).Take(AgentLimits.MaxUnifiDevices)];
            clients = Usage(UnifiParsers.ParseClients(await client.GetAsync(path + "stat/sta", cancellationToken)), now);
        }

        return new UnifiReport
        {
            Site = _siteName,
            Version = _version,
            WanRxBytesPerSec = health.WanRx,
            WanTxBytesPerSec = health.WanTx,
            WiredClients = health.Wired,
            WirelessClients = health.Wireless,
            GuestClients = health.Guests,
            Devices = devices,
            Clients = clients,
        };
    }

    /// <summary>The site's name and UniFi Network's version, from the integration API; neither is essential.</summary>
    private async Task DescribeSiteAsync(CancellationToken cancellationToken)
    {
        try
        {
            _version = UnifiParsers.Version(await client.GetAsync("integration/v1/info", cancellationToken)) ?? _version;
            _siteName = UnifiParsers.SiteName(await client.GetAsync("integration/v1/sites", cancellationToken), site) ?? _siteName;
        }
        catch (UnifiException ex) when (ex.Status is not null)
        {
        }
    }

    /// <summary>
    /// What each client moved since the last report. The first report only sets the counters, so it has no
    /// clients rather than every client's total since it connected.
    /// </summary>
    internal List<UnifiClientUsage>? Usage(IReadOnlyList<UnifiClientCounters> counters, DateTimeOffset now)
    {
        var previousAt = _countersAt;
        var previous = new Dictionary<string, (long Downloaded, long Uploaded)>(_counters, StringComparer.OrdinalIgnoreCase);
        _counters.Clear();
        foreach (var counter in counters)
        {
            _counters[counter.Mac] = (counter.Downloaded, counter.Uploaded);
        }

        _countersAt = now;
        if (previousAt is not { } since || now <= since)
        {
            return null;
        }

        var seconds = (now - since).TotalSeconds;
        return counters
            .Select(counter =>
            {
                var (downloaded, uploaded) = previous.TryGetValue(counter.Mac, out var before)
                    ? (Delta(counter.Downloaded, before.Downloaded), Delta(counter.Uploaded, before.Uploaded))
                    : (0L, 0L);
                return new UnifiClientUsage
                {
                    Mac = counter.Mac,
                    Name = counter.Name,
                    Ip = counter.Ip,
                    Wired = counter.Wired,
                    Network = counter.Network,
                    DownloadedBytes = downloaded,
                    UploadedBytes = uploaded,
                    IntervalSeconds = seconds,
                };
            })
            .Take(AgentLimits.MaxUnifiClients)
            .ToList();
    }

    /// <summary>Counters only grow while a client stays connected; a smaller one means it started again.</summary>
    private static long Delta(long now, long before) => now >= before ? now - before : now;

    public void Dispose() => client.Dispose();
}
