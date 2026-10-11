using System.Buffers.Binary;
using System.Text;

namespace UsbGScope.Core.Descriptors;

/// <summary>Standard USB device descriptor (USB 2.0 spec §9.6.1).</summary>
public readonly record struct DeviceDescriptor(
    ushort BcdUsb,
    byte DeviceClass,
    byte DeviceSubClass,
    byte DeviceProtocol,
    byte MaxPacketSize0,
    ushort VendorId,
    ushort ProductId,
    ushort BcdDevice,
    byte ManufacturerIndex,
    byte ProductIndex,
    byte SerialNumberIndex,
    byte NumConfigurations);

/// <summary>Pure parsers for raw descriptor bytes. No I/O, so they are unit-testable.</summary>
public static class UsbDescriptorParser
{
    public const byte DeviceType = 0x01;
    public const byte ConfigurationType = 0x02;
    public const byte StringType = 0x03;

    public static DeviceDescriptor? ParseDevice(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18)
            return null;

        return new DeviceDescriptor(
            BcdUsb: BinaryPrimitives.ReadUInt16LittleEndian(data[2..]),
            DeviceClass: data[4],
            DeviceSubClass: data[5],
            DeviceProtocol: data[6],
            MaxPacketSize0: data[7],
            VendorId: BinaryPrimitives.ReadUInt16LittleEndian(data[8..]),
            ProductId: BinaryPrimitives.ReadUInt16LittleEndian(data[10..]),
            BcdDevice: BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            ManufacturerIndex: data[14],
            ProductIndex: data[15],
            SerialNumberIndex: data[16],
            NumConfigurations: data[17]);
    }

    /// <summary>Decodes a UTF-16LE string descriptor; returns null if malformed or empty.</summary>
    public static string? ParseString(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2 || data[1] != StringType)
            return null;

        int length = Math.Min(data[0], data.Length);
        if (length <= 2)
            return null;

        string text = Encoding.Unicode.GetString(data[2..(2 + ((length - 2) & ~1))]).TrimEnd('\0').Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>First LANGID from string descriptor zero, or null.</summary>
    public static ushort? ParseFirstLanguageId(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[1] != StringType || data[0] < 4)
            return null;
        return BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
    }

    /// <summary>
    /// Max power from a configuration descriptor header, in mA.
    /// bMaxPower is in 2 mA units at USB 2.0 speeds and 8 mA units at SuperSpeed.
    /// </summary>
    public static int? ParseMaxPowerMilliamps(ReadOnlySpan<byte> data, bool superSpeed)
    {
        if (data.Length < 9 || data[1] != ConfigurationType)
            return null;
        return data[8] * (superSpeed ? 8 : 2);
    }

    /// <summary>Formats a BCD version such as 0x0320 as "3.20".</summary>
    public static string FormatBcd(ushort bcd) => $"{bcd >> 8:X}.{bcd & 0xFF:X2}";
}
