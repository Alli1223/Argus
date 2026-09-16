using System.Runtime.InteropServices;
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

    /// <summary>The value of every instance of a wildcard counter path, such as <c>\Thermal Zone Information(*)\Temperature</c>.</summary>
    public unsafe List<(string Instance, double Value)> ReadInstances(string counterPath)
    {
        var values = new List<(string Instance, double Value)>();
        if (!_counters.TryGetValue(counterPath, out var counter))
        {
            return values;
        }

        uint size = 0;
        if (WindowsNative.PdhGetFormattedCounterArray(counter, WindowsNative.PdhFormatDouble, ref size, out _, null) != WindowsNative.PdhMoreData
            || size == 0)
        {
            return values;
        }

        // The names the items point to are stored in the same buffer, after the items.
        var buffer = (WindowsNative.PdhCounterValueItem*)NativeMemory.Alloc(size);
        try
        {
            if (WindowsNative.PdhGetFormattedCounterArray(counter, WindowsNative.PdhFormatDouble, ref size, out var count, buffer) != 0)
            {
                return values;
            }

            for (var index = 0; index < count; index++)
            {
                var item = buffer[index];
                if (item.Value.Status is 0 or 1 && Marshal.PtrToStringUni(item.Name) is { } name)
                {
                    values.Add((name, item.Value.DoubleValue));
                }
            }

            return values;
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }

    public void Dispose() => WindowsNative.PdhCloseQuery(_query);
}
