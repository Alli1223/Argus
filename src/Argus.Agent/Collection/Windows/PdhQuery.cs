using System.Runtime.Versioning;

namespace Argus.Agent.Collection.Windows;

/// <summary>
/// A Performance Data Helper query over English counter paths, which work regardless of the
/// display language Windows is installed in.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PdhQuery : IDisposable
{
    private readonly nint _query;
    private readonly Dictionary<string, nint> _counters = new(StringComparer.OrdinalIgnoreCase);

    public PdhQuery(IEnumerable<string> counterPaths)
    {
        var status = WindowsNative.PdhOpenQuery(null, 0, out _query);
        if (status != 0)
        {
            throw new InvalidOperationException($"PdhOpenQuery failed (0x{status:X8}).");
        }

        foreach (var path in counterPaths)
        {
            if (WindowsNative.PdhAddEnglishCounter(_query, path, 0, out var counter) == 0)
            {
                _counters[path] = counter;
            }
        }

        // Rate counters only report a value once they have two collections to compare.
        WindowsNative.PdhCollectQueryData(_query);
    }

    public void Collect() => WindowsNative.PdhCollectQueryData(_query);

    public double? Read(string counterPath) =>
        _counters.TryGetValue(counterPath, out var counter)
        && WindowsNative.PdhGetFormattedCounterValue(counter, WindowsNative.PdhFormatDouble, out _, out var value) == 0
        && value.Status is 0 or 1
            ? value.DoubleValue
            : null;

    public void Dispose() => WindowsNative.PdhCloseQuery(_query);
}
