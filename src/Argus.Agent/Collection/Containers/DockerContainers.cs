using System.Diagnostics;
using System.Text.Json;
using Argus.Contracts.Agent;

namespace Argus.Agent.Collection.Containers;

/// <summary>A machine's containers for one sample: the report when it is due, and what running containers used.</summary>
internal sealed record ContainerReading(ContainerReport? Report, IReadOnlyList<ContainerUsage>? Usage)
{
    public static readonly ContainerReading None = new(null, null);
}

internal interface IContainerSource
{
    /// <summary>Takes a first reading of cumulative counters so the next <see cref="Collect"/> can compute rates.</summary>
    void Prime();

    ContainerReading Collect();
}

/// <summary>For machines whose containers the agent does not watch.</summary>
internal sealed class NoContainers : IContainerSource
{
    public void Prime()
    {
    }

    public ContainerReading Collect() => ContainerReading.None;
}

/// <summary>
/// Lists Docker's containers and what they use. The full list goes to the server when it changes, and at
/// least once a minute; usage goes with every sample. Machines without Docker's socket report nothing, so
/// Docker installed later is picked up without restarting the agent.
/// </summary>
internal sealed class DockerContainers(
    string socketPath, bool actionsEnabled, TimeProvider time, ILogger<DockerContainers> logger, DockerClient? client = null)
    : IContainerSource, IDisposable
{
    public static readonly TimeSpan ReportInterval = TimeSpan.FromMinutes(1);

    /// <summary>Stats and inspections run side by side, but not so many that Docker notices.</summary>
    private const int Parallelism = 8;

    private static readonly TimeSpan CollectTimeout = TimeSpan.FromSeconds(10);

    private readonly DockerClient _docker = client ?? new DockerClient(socketPath);

    /// <summary>The socket to look for, unless a client was handed in (as tests do).</summary>
    private readonly string? _socketPath = client is null ? socketPath : null;
    private Dictionary<string, ContainerCounters> _counters = [];
    private long _lastReading;
    private string? _lastReport;
    private DateTimeOffset _lastReportAt = DateTimeOffset.MinValue;
    private string? _lastProblem;

    public ContainerReading Collect()
    {
        if (_socketPath is not null && !File.Exists(_socketPath))
        {
            return ContainerReading.None;
        }

        using var timeout = new CancellationTokenSource(CollectTimeout);
        try
        {
            return CollectAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is DockerException or JsonException or OperationCanceledException or KeyNotFoundException or InvalidOperationException)
        {
            var problem = ex is OperationCanceledException ? "Docker did not answer within 10 seconds." : ex.Message;
            if (problem != _lastProblem)
            {
                logger.LogWarning("Could not read Docker's containers: {Problem}", problem);
                _lastProblem = problem;
            }

            _counters = [];
            return new ContainerReading(Due(problem) ? new ContainerReport { Problem = problem, ActionsEnabled = actionsEnabled } : null, null);
        }
    }

    public void Prime()
    {
        Collect();

        // Only counters were wanted: the first sample still carries the report.
        _lastReport = null;
        _lastReportAt = DateTimeOffset.MinValue;
    }

    public void Dispose() => _docker.Dispose();

    private async Task<ContainerReading> CollectAsync(CancellationToken cancellationToken)
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = _lastReading == 0 ? 0 : Stopwatch.GetElapsedTime(_lastReading, now).TotalSeconds;
        _lastReading = now;

        using var version = await _docker.GetJsonAsync("version", cancellationToken);
        using var list = await _docker.GetJsonAsync("containers/json?all=1", cancellationToken);
        var listed = list.RootElement.EnumerateArray().Take(AgentLimits.MaxContainers).ToList();

        var containers = new ContainerInfo?[listed.Count];
        var usage = new ContainerUsage?[listed.Count];
        var counters = new ContainerCounters?[listed.Count];
        using var throttle = new SemaphoreSlim(Parallelism);

        await Task.WhenAll(listed.Select(async (entry, index) =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var id = entry.GetProperty("Id").GetString()!;
                JsonDocument inspected;
                try
                {
                    inspected = await _docker.GetJsonAsync($"containers/{id}/json", cancellationToken);
                }
                catch (DockerException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound)
                {
                    return; // Removed since the list was taken.
                }

                using (inspected)
                {
                    var container = DockerParsers.ParseContainer(entry, inspected.RootElement);
                    containers[index] = container;
                    if (container.State != "running")
                    {
                        return;
                    }

                    using var stats = await _docker.GetJsonAsync($"containers/{id}/stats?stream=false&one-shot=true", cancellationToken);
                    usage[index] = _counters.TryGetValue(id, out var previous)
                        ? DockerParsers.Usage(container.Name, stats.RootElement, previous, seconds)
                        : null;
                    counters[index] = DockerParsers.ParseCounters(stats.RootElement);
                }
            }
            catch (DockerException ex) when (ex.Status == System.Net.HttpStatusCode.NotFound || ex.Status == System.Net.HttpStatusCode.Conflict)
            {
                // Stopped or removed part way; the next reading sees it as it is.
            }
            finally
            {
                throttle.Release();
            }
        }));

        _counters = listed
            .Select((entry, index) => (Id: entry.GetProperty("Id").GetString()!, Counters: counters[index]))
            .Where(pair => pair.Counters is not null)
            .ToDictionary(pair => pair.Id, pair => pair.Counters!.Value);
        _lastProblem = null;

        var items = containers.OfType<ContainerInfo>().OrderBy(container => container.Name, StringComparer.Ordinal).ToList();
        var report = new ContainerReport
        {
            EngineVersion = version.RootElement.TryGetProperty("Version", out var engine) ? engine.GetString() : null,
            ActionsEnabled = actionsEnabled,
            Items = items,
        };

        return new ContainerReading(
            Due(JsonSerializer.Serialize(report, AgentJsonContext.Default.ContainerReport)) ? report : null,
            usage.OfType<ContainerUsage>().ToList());
    }

    /// <summary>Whether a report with this content should go out now: when it changed, or a minute after the last.</summary>
    private bool Due(string content)
    {
        var now = time.GetUtcNow();
        if (content == _lastReport && now - _lastReportAt < ReportInterval)
        {
            return false;
        }

        _lastReport = content;
        _lastReportAt = now;
        return true;
    }
}
