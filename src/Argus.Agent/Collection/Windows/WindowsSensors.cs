using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Argus.Agent.Collection.Windows;

/// <summary>A graphics adapter's temperature, as its driver reports it to Windows.</summary>
internal sealed record GpuReading(string Name, double Celsius);

/// <summary>A drive's temperature sensors, as Windows' storage drivers report them.</summary>
internal sealed record DriveReading(int Number, string Model, IReadOnlyList<double> Celsius);

/// <summary>
/// Graphics adapter and drive temperatures through Windows' own interfaces: the adapter performance data
/// Task Manager shows (Windows 10 2004 and later, WDDM 2.7 drivers) and the storage drivers' temperature
/// query. Neither needs a vendor's tools or a driver of Argus's own.
/// </summary>
[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsSensors
{
    /// <summary>\\.\PhysicalDrive0 to this, less one, are looked at; machines rarely number drives beyond it.</summary>
    private const int MaxDrives = 32;

    private const int KmtqaitypeAdapterRegistryInfo = 8;
    private const int KmtqaitypeAdapterPerfData = 62;
    private const int AdapterRegistryInfoLength = 4 * 260;

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const int StorageDeviceProperty = 0;
    private const int StorageDeviceTemperatureProperty = 52;
    private const uint FileShareReadWrite = 0x1 | 0x2;
    private const uint OpenExisting = 3;

#pragma warning disable CS0649 // Written by native code.
    [StructLayout(LayoutKind.Sequential)]
    private struct EnumAdapters2
    {
        public uint NumAdapters;
        public AdapterInfo* Adapters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterInfo
    {
        public uint Adapter;
        public uint LuidLow;
        public int LuidHigh;
        public uint NumOfSources;
        public int PrecisePresentRegionsPreferred;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public void* PrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    /// <summary>D3DKMT_ADAPTER_PERFDATA.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterPerfData
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOc;
        public ulong MemoryBandwidth;
        public ulong PcieBandwidth;
        public uint FanRpm;
        public uint Power;

        /// <summary>Tenths of a degree Celsius; 0 where the driver does not report it.</summary>
        public uint Temperature;

        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }
#pragma warning restore CS0649

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTEnumAdapters2(EnumAdapters2* adapters);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTQueryAdapterInfo(QueryAdapterInfo* info);

    [LibraryImport("gdi32.dll")]
    private static partial int D3DKMTCloseAdapter(uint* adapter);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, void* inBuffer, uint inBufferSize, void* outBuffer, uint outBufferSize, out uint bytesReturned, nint overlapped);

    /// <summary>Every graphics adapter that reports a temperature, in Windows' order.</summary>
    public static List<GpuReading> ReadGpus()
    {
        var enumeration = new EnumAdapters2();
        if (D3DKMTEnumAdapters2(&enumeration) != 0 || enumeration.NumAdapters == 0)
        {
            return [];
        }

        var adapters = new AdapterInfo[enumeration.NumAdapters];
        var readings = new List<GpuReading>();
        fixed (AdapterInfo* pointer = adapters)
        {
            enumeration.Adapters = pointer;
            if (D3DKMTEnumAdapters2(&enumeration) != 0)
            {
                return [];
            }

            var seen = new HashSet<(uint, int)>();
            for (var i = 0; i < enumeration.NumAdapters; i++)
            {
                var adapter = pointer[i].Adapter;
                try
                {
                    if (seen.Add((pointer[i].LuidLow, pointer[i].LuidHigh))
                        && Temperature(adapter) is { } celsius)
                    {
                        readings.Add(new GpuReading(AdapterName(adapter) ?? "GPU", celsius));
                    }
                }
                finally
                {
                    D3DKMTCloseAdapter(&adapter);
                }
            }
        }

        return readings;
    }

    private static double? Temperature(uint adapter)
    {
        var data = new AdapterPerfData();
        var query = new QueryAdapterInfo
        {
            Adapter = adapter,
            Type = KmtqaitypeAdapterPerfData,
            PrivateDriverData = &data,
            PrivateDriverDataSize = (uint)sizeof(AdapterPerfData),
        };
        return D3DKMTQueryAdapterInfo(&query) == 0 ? WindowsSensorData.GpuCelsius(data.Temperature) : null;
    }

    private static string? AdapterName(uint adapter)
    {
        // D3DKMT_ADAPTERREGISTRYINFO: the adapter, BIOS, DAC and chip names, MAX_PATH characters each.
        var buffer = new char[AdapterRegistryInfoLength];
        fixed (char* pointer = buffer)
        {
            var query = new QueryAdapterInfo
            {
                Adapter = adapter,
                Type = KmtqaitypeAdapterRegistryInfo,
                PrivateDriverData = pointer,
                PrivateDriverDataSize = (uint)(buffer.Length * sizeof(char)),
            };
            return D3DKMTQueryAdapterInfo(&query) == 0 ? WindowsSensorData.AdapterName(buffer.AsSpan(0, 260)) : null;
        }
    }

    /// <summary>
    /// The temperatures of the machine's drives. NVMe drives are always read; others only when
    /// <paramref name="includeOthers"/> is set, as asking a SATA drive can keep it from spinning down.
    /// </summary>
    public static List<DriveReading> ReadDrives(bool includeOthers)
    {
        var readings = new List<DriveReading>();
        Span<byte> buffer = stackalloc byte[1024];
        for (var number = 0; number < MaxDrives; number++)
        {
            // No access rights are needed to query a drive's properties, and asking for none disturbs nothing.
            using var drive = CreateFile($@"\\.\PhysicalDrive{number}", 0, FileShareReadWrite, 0, OpenExisting, 0, 0);
            if (drive.IsInvalid)
            {
                continue;
            }

            if (Query(drive, StorageDeviceProperty, buffer) is not { } device
                || WindowsSensorData.ParseDeviceDescriptor(buffer[..device]) is not { } descriptor
                || !WindowsSensorData.ShouldRead(descriptor.BusType, includeOthers))
            {
                continue;
            }

            if (Query(drive, StorageDeviceTemperatureProperty, buffer) is { } length
                && WindowsSensorData.ParseTemperatures(buffer[..length]) is { Count: > 0 } temperatures)
            {
                readings.Add(new DriveReading(number, descriptor.Model, temperatures));
            }
        }

        return readings;
    }

    private static int? Query(SafeFileHandle drive, int propertyId, Span<byte> buffer)
    {
        var query = new StoragePropertyQuery { PropertyId = propertyId };
        buffer.Clear();
        fixed (byte* pointer = buffer)
        {
            return DeviceIoControl(drive, IoctlStorageQueryProperty, &query, (uint)sizeof(StoragePropertyQuery),
                    pointer, (uint)buffer.Length, out var returned, 0)
                ? (int)returned
                : null;
        }
    }
}

/// <summary>What Windows' sensor structures hold, kept apart from the calls that fill them so they can be tested anywhere.</summary>
internal static class WindowsSensorData
{
    /// <summary>STORAGE_BUS_TYPE values the agent tells apart.</summary>
    public const int BusTypeUsb = 7;
    public const int BusTypeSata = 11;
    public const int BusTypeVirtual = 14;
    public const int BusTypeFileBackedVirtual = 15;
    public const int BusTypeSpaces = 16;
    public const int BusTypeNvme = 17;

    public sealed record DeviceDescriptor(int BusType, string Model);

    /// <summary>An adapter's temperature from tenths of a degree; null for 0, which means none was reported.</summary>
    public static double? GpuCelsius(uint deciCelsius) =>
        deciCelsius != 0 && TemperatureReadings.IsPlausible(deciCelsius / 10.0) ? deciCelsius / 10.0 : null;

    /// <summary>The adapter's name from the first field of D3DKMT_ADAPTERREGISTRYINFO, when it holds a readable one.</summary>
    public static string? AdapterName(ReadOnlySpan<char> field)
    {
        var end = field.IndexOf('\0');
        var name = (end < 0 ? field : field[..end]).ToString().Trim();
        return name.Length > 0 && !name.Any(char.IsControl) ? name : null;
    }

    /// <summary>The bus and model from a STORAGE_DEVICE_DESCRIPTOR; null when the buffer is too short to be one.</summary>
    public static DeviceDescriptor? ParseDeviceDescriptor(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 36)
        {
            return null;
        }

        var vendor = AsciiAt(buffer, BinaryPrimitives.ReadInt32LittleEndian(buffer[12..]));
        var product = AsciiAt(buffer, BinaryPrimitives.ReadInt32LittleEndian(buffer[16..]));
        var model = (product, vendor) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) when !IsGenericVendor(vendor) && !product.StartsWith(vendor, StringComparison.OrdinalIgnoreCase) =>
                $"{vendor} {product}",
            ({ Length: > 0 }, _) => product,
            (_, { Length: > 0 }) => vendor,
            _ => "Drive",
        };
        return new DeviceDescriptor(BinaryPrimitives.ReadInt32LittleEndian(buffer[28..]), model);
    }

    /// <summary>
    /// Whether to ask a drive on this bus for its temperature. NVMe drives answer without waking anything;
    /// SATA and SAS drives are asked only when the agent is told to. USB enclosures, virtual disks and
    /// Storage Spaces are never asked: they rarely answer, and their drives are read on their own.
    /// </summary>
    public static bool ShouldRead(int busType, bool includeOthers) => busType switch
    {
        BusTypeNvme => true,
        BusTypeUsb or BusTypeVirtual or BusTypeFileBackedVirtual or BusTypeSpaces => false,
        _ => includeOthers,
    };

    /// <summary>The believable readings in a STORAGE_TEMPERATURE_DATA_DESCRIPTOR, in degrees Celsius.</summary>
    public static List<double> ParseTemperatures(ReadOnlySpan<byte> buffer)
    {
        const int header = 24;
        const int infoLength = 16;
        if (buffer.Length < header)
        {
            return [];
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(buffer[12..]);
        var readings = new List<double>();
        for (var i = 0; i < count && header + (i + 1) * infoLength <= buffer.Length; i++)
        {
            // Each STORAGE_TEMPERATURE_INFO starts with its index, then the temperature in whole degrees.
            var celsius = BinaryPrimitives.ReadInt16LittleEndian(buffer[(header + i * infoLength + 2)..]);
            if (celsius != 0 && TemperatureReadings.IsPlausible(celsius))
            {
                readings.Add(celsius);
            }
        }

        return readings;
    }

    // Windows names SATA drives' vendor "ATA" and some NVMe drives' "NVMe": neither is a maker.
    private static bool IsGenericVendor(string vendor) =>
        vendor.Equals("ATA", StringComparison.OrdinalIgnoreCase) || vendor.Equals("NVMe", StringComparison.OrdinalIgnoreCase);

    private static string AsciiAt(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset <= 0 || offset >= buffer.Length)
        {
            return "";
        }

        var text = buffer[offset..];
        var end = text.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? text : text[..end]).Trim();
    }
}
