using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Argus.Agent.Collection.Windows;

/// <summary>The few Win32 and PDH functions the Windows collectors need.</summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsNative
{
    public const uint PdhFormatDouble = 0x00000200;

    /// <summary>PDH_MORE_DATA: the buffer passed was too small (or missing); its needed size was returned.</summary>
    public const uint PdhMoreData = 0x800007D2;

    private const int RelationProcessorCore = 0;

#pragma warning disable CS0649 // Written by native code.
    [StructLayout(LayoutKind.Sequential)]
    public struct FileTime
    {
        public uint Low;
        public uint High;

        public readonly ulong Ticks => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct PdhCounterValue
    {
        [FieldOffset(0)]
        public uint Status;

        [FieldOffset(8)]
        public double DoubleValue;
    }

    /// <summary>One instance's value in the array <see cref="PdhGetFormattedCounterArray"/> fills.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PdhCounterValueItem
    {
        public nint Name;
        public PdhCounterValue Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LogicalProcessorInformation
    {
        public nuint ProcessorMask;
        public int Relationship;
        public ulong Reserved1;
        public ulong Reserved2;
    }
#pragma warning restore CS0649

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLogicalProcessorInformation(LogicalProcessorInformation* buffer, ref uint returnLength);

    [LibraryImport("pdh.dll", EntryPoint = "PdhOpenQueryW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhOpenQuery(string? dataSource, nint userData, out nint query);

    [LibraryImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint PdhAddEnglishCounter(nint query, string counterPath, nint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhGetFormattedCounterValue(nint counter, uint format, out uint type, out PdhCounterValue value);

    [LibraryImport("pdh.dll", EntryPoint = "PdhGetFormattedCounterArrayW")]
    public static partial uint PdhGetFormattedCounterArray(
        nint counter, uint format, ref uint bufferSize, out uint itemCount, PdhCounterValueItem* items);

    [LibraryImport("pdh.dll")]
    public static partial uint PdhCloseQuery(nint query);

    public static MemoryStatusEx GetMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)sizeof(MemoryStatusEx) };
        return GlobalMemoryStatusEx(ref status) ? status : throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    /// <summary>Physical cores in the first processor group (enough for all but the largest servers).</summary>
    public static int? CountPhysicalCores()
    {
        uint length = 0;
        GetLogicalProcessorInformation(null, ref length);
        var count = (int)(length / (uint)sizeof(LogicalProcessorInformation));
        if (count == 0)
        {
            return null;
        }

        var buffer = new LogicalProcessorInformation[count];
        fixed (LogicalProcessorInformation* pointer = buffer)
        {
            if (!GetLogicalProcessorInformation(pointer, ref length))
            {
                return null;
            }
        }

        return buffer.Count(info => info.Relationship == RelationProcessorCore);
    }
}
