using System.Buffers.Binary;
using System.Text;
using Argus.Agent.Collection.Windows;

namespace Argus.Agent.Tests.Collection;

/// <summary>Reads Windows' sensor structures as the drivers fill them.</summary>
public sealed class WindowsSensorDataTests
{
    /// <summary>A STORAGE_DEVICE_DESCRIPTOR with its strings after the fixed part.</summary>
    private static byte[] Descriptor(int busType, string? vendor, string? product)
    {
        var buffer = new byte[256];
        BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28), busType);
        var offset = 40;
        foreach (var (field, text) in new[] { (12, vendor), (16, product) })
        {
            if (text is null)
            {
                continue;
            }

            BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(field), offset);
            offset += Encoding.ASCII.GetBytes(text, buffer.AsSpan(offset)) + 1;
        }

        return buffer;
    }

    /// <summary>A STORAGE_TEMPERATURE_DATA_DESCRIPTOR holding these readings.</summary>
    private static byte[] Temperatures(params short[] celsius)
    {
        var buffer = new byte[24 + 16 * celsius.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(12), (ushort)celsius.Length);
        for (var i = 0; i < celsius.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(24 + i * 16), (ushort)i);
            BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(24 + i * 16 + 2), celsius[i]);
        }

        return buffer;
    }

    [Fact]
    public void NvmeDrivesGoByTheirProductName()
    {
        var descriptor = WindowsSensorData.ParseDeviceDescriptor(Descriptor(WindowsSensorData.BusTypeNvme, "NVMe", "Samsung SSD 980 PRO 1TB  "));

        Assert.Equal(new WindowsSensorData.DeviceDescriptor(WindowsSensorData.BusTypeNvme, "Samsung SSD 980 PRO 1TB"), descriptor);
    }

    [Theory]
    [InlineData("ATA", "WDC WD40EFRX-68N32N0", "WDC WD40EFRX-68N32N0")]
    [InlineData("SEAGATE", "ST4000NM0035", "SEAGATE ST4000NM0035")]
    [InlineData("Kingston", "Kingston A2000", "Kingston A2000")]
    [InlineData(null, null, "Drive")]
    [InlineData("WD", null, "WD")]
    public void ModelsLeaveOutGenericVendors(string? vendor, string? product, string expected) =>
        Assert.Equal(expected, WindowsSensorData.ParseDeviceDescriptor(Descriptor(WindowsSensorData.BusTypeSata, vendor, product))!.Model);

    [Fact]
    public void ShortBuffersAreNotDescriptors() => Assert.Null(WindowsSensorData.ParseDeviceDescriptor(new byte[20]));

    [Theory]
    [InlineData(WindowsSensorData.BusTypeNvme, false, true)]
    [InlineData(WindowsSensorData.BusTypeSata, false, false)]
    [InlineData(WindowsSensorData.BusTypeSata, true, true)]
    [InlineData(WindowsSensorData.BusTypeUsb, true, false)]
    [InlineData(WindowsSensorData.BusTypeSpaces, true, false)]
    public void SataDrivesAreReadOnlyWhenAskedFor(int busType, bool includeOthers, bool expected) =>
        Assert.Equal(expected, WindowsSensorData.ShouldRead(busType, includeOthers));

    [Fact]
    public void DriveReadingsDropMissingAndImpossibleValues() =>
        Assert.Equal([38.0, 51.0], WindowsSensorData.ParseTemperatures(Temperatures(38, 0, -128, 51)));

    [Fact]
    public void DriveReadingsStopAtTheBuffersEnd() =>
        Assert.Equal([40.0], WindowsSensorData.ParseTemperatures(Temperatures(40, 41).AsSpan(0, 24 + 16)));

    [Theory]
    [InlineData(0u, null)]
    [InlineData(452u, 45.2)]
    [InlineData(2000u, null)]
    public void AdaptersReportTenthsOfADegree(uint deciCelsius, double? expected) =>
        Assert.Equal(expected, WindowsSensorData.GpuCelsius(deciCelsius));

    [Fact]
    public void AdapterNamesEndAtTheirTerminator()
    {
        var field = new char[260];
        "NVIDIA GeForce RTX 3080".CopyTo(field);

        Assert.Equal("NVIDIA GeForce RTX 3080", WindowsSensorData.AdapterName(field));
        Assert.Null(WindowsSensorData.AdapterName(new char[260]));
        Assert.Null(WindowsSensorData.AdapterName("\u0001\u0002"));
    }
}
