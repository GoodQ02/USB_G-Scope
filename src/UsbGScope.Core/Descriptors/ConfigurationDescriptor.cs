using System.Buffers.Binary;

namespace UsbGScope.Core.Descriptors;

public enum UsbTransferType
{
    Control = 0,
    Isochronous = 1,
    Bulk = 2,
    Interrupt = 3,
}

/// <summary>Any descriptor we keep as raw bytes (class-specific, SS companion, unknown).</summary>
public sealed record RawDescriptor(byte Type, byte[] Data);

/// <summary>Endpoint descriptor (USB 2.0 spec §9.6.6).</summary>
public sealed record EndpointDescriptor(byte Address, byte Attributes, ushort MaxPacketSize, byte Interval)
{
    public int Number => Address & 0x0F;
    public bool IsIn => (Address & 0x80) != 0;
    public UsbTransferType TransferType => (UsbTransferType)(Attributes & 0x03);

    /// <summary>Bits 10..0 of wMaxPacketSize; bits 12..11 encode extra high-bandwidth transactions.</summary>
    public int PacketBytes => MaxPacketSize & 0x07FF;
}

/// <summary>Interface descriptor (§9.6.5) with the endpoints and class descriptors that follow it.</summary>
public sealed record InterfaceDescriptor(
    byte InterfaceNumber,
    byte AlternateSetting,
    byte InterfaceClass,
    byte InterfaceSubClass,
    byte InterfaceProtocol,
    byte InterfaceStringIndex,
    IReadOnlyList<EndpointDescriptor> Endpoints,
    IReadOnlyList<RawDescriptor> ExtraDescriptors)
{
    public string ClassName => UsbClassNames.Get(InterfaceClass);
}

/// <summary>Interface Association Descriptor (IAD), used by composite functions like webcams.</summary>
public sealed record InterfaceAssociation(
    byte FirstInterface,
    byte InterfaceCount,
    byte FunctionClass,
    byte FunctionSubClass,
    byte FunctionProtocol,
    byte FunctionStringIndex)
{
    public string ClassName => UsbClassNames.Get(FunctionClass);
}

/// <summary>A fully decoded configuration descriptor set (§9.6.3).</summary>
public sealed record ConfigurationDescriptor(
    ushort TotalLength,
    byte NumInterfaces,
    byte ConfigurationValue,
    byte ConfigurationStringIndex,
    byte Attributes,
    byte MaxPowerRaw,
    IReadOnlyList<InterfaceDescriptor> Interfaces,
    IReadOnlyList<InterfaceAssociation> Associations,
    IReadOnlyList<RawDescriptor> ConfigurationExtras,
    bool IsTruncated)
{
    public bool SelfPowered => (Attributes & 0x40) != 0;
    public bool RemoteWakeup => (Attributes & 0x20) != 0;

    /// <summary>bMaxPower is in 2 mA units at USB 2.0 speeds and 8 mA units at SuperSpeed.</summary>
    public int MaxPowerMilliamps(bool superSpeed) => MaxPowerRaw * (superSpeed ? 8 : 2);
}

public static class ConfigurationDescriptorParser
{
    private const byte InterfaceType = 0x04;
    private const byte EndpointType = 0x05;
    private const byte InterfaceAssociationType = 0x0B;

    /// <summary>
    /// Parses a configuration descriptor and everything that follows it. Tolerates malformed input:
    /// parsing stops at the first descriptor with an invalid length and <see cref="ConfigurationDescriptor.IsTruncated"/> is set.
    /// </summary>
    public static ConfigurationDescriptor? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 9 || data[0] < 9 || data[1] != UsbDescriptorParser.ConfigurationType)
            return null;

        ushort totalLength = BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);
        int end = Math.Min(totalLength, data.Length);
        bool truncated = totalLength > data.Length;

        var interfaces = new List<InterfaceDescriptor>();
        var associations = new List<InterfaceAssociation>();
        var configExtras = new List<RawDescriptor>();

        ReadOnlySpan<byte> current = default;
        List<EndpointDescriptor>? endpoints = null;
        List<RawDescriptor>? extras = null;

        void FlushInterface(ReadOnlySpan<byte> header)
        {
            if (endpoints is null || extras is null)
                return;
            interfaces.Add(new InterfaceDescriptor(header[2], header[3], header[5], header[6], header[7], header[8], endpoints, extras));
        }

        int offset = data[0];
        while (offset + 2 <= end)
        {
            int length = data[offset];
            if (length < 2 || offset + length > end)
            {
                truncated = true;
                break;
            }

            var d = data.Slice(offset, length);
            byte type = d[1];

            if (type == InterfaceType && length >= 9)
            {
                FlushInterface(current);
                current = d;
                endpoints = [];
                extras = [];
            }
            else if (type == EndpointType && length >= 7 && endpoints is not null)
            {
                endpoints.Add(new EndpointDescriptor(d[2], d[3], BinaryPrimitives.ReadUInt16LittleEndian(d[4..]), d[6]));
            }
            else if (type == InterfaceAssociationType && length >= 8)
            {
                associations.Add(new InterfaceAssociation(d[2], d[3], d[4], d[5], d[6], d[7]));
            }
            else
            {
                (extras ?? configExtras).Add(new RawDescriptor(type, d.ToArray()));
            }

            offset += length;
        }

        FlushInterface(current);

        return new ConfigurationDescriptor(
            totalLength, data[4], data[5], data[6], data[7], data[8],
            interfaces, associations, configExtras, truncated);
    }
}
