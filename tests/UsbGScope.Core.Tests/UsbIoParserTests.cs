using System.Buffers.Binary;
using System.Text;
using UsbGScope.Core.Enumeration;
using UsbGScope.Core.Interop;
using UsbGScope.Core.Model;

namespace UsbGScope.Core.Tests;

/// <summary>Guards the usbioctl.h byte offsets used to decode hub IOCTL buffers.</summary>
public class UsbIoParserTests
{
    [Fact]
    public void Decodes_connection_information_ex_offsets()
    {
        var buffer = new byte[UsbIoParsers.ConnectionInfoExFixedSize + 4 * UsbIoParsers.PipeInfoSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 3);                            // ConnectionIndex
        Fixture.Load("logitech-c548.device").CopyTo(buffer, 4);                         // DeviceDescriptor
        buffer[22] = 1;                                                                 // CurrentConfigurationValue
        buffer[23] = 2;                                                                 // Speed = high (distinct from config value)
        buffer[24] = 0;                                                                 // DeviceIsHub (neighbours are non-zero)
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(25), 7);                 // DeviceAddress
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(27), 4);                 // NumberOfOpenPipes
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(31), 1);                 // DeviceConnected

        var info = UsbIoParsers.ParseConnectionInfoEx(buffer)!.Value;

        Assert.Equal(UsbConnectionStatus.DeviceConnected, info.Status);
        Assert.Equal(Fixture.Load("logitech-c548.device"), info.DeviceDescriptor);
        Assert.Equal(1, info.CurrentConfiguration);
        Assert.Equal(2, info.SpeedCode);
        Assert.False(info.IsHub);
        Assert.Equal(7, info.DeviceAddress);
        Assert.Equal(4u, info.OpenPipes);
    }

    [Fact]
    public void Short_connection_buffer_is_rejected()
    {
        Assert.Null(UsbIoParsers.ParseConnectionInfoEx(new byte[UsbIoParsers.ConnectionInfoExFixedSize - 1]));
    }

    private static byte[] NameBuffer(int header, string name, int? actualLength = null, int extra = 0)
    {
        var chars = Encoding.Unicode.GetBytes(name + "\0");
        var buffer = new byte[header + chars.Length + extra];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(header - 4), (uint)(actualLength ?? buffer.Length));
        chars.CopyTo(buffer, header);
        return buffer;
    }

    [Theory]
    [InlineData(4)] // USB_ROOT_HUB_NAME
    [InlineData(8)] // USB_NODE_CONNECTION_NAME / DRIVERKEY_NAME
    public void Decodes_variable_length_names(int header)
    {
        const string name = @"USB#ROOT_HUB30#4&2a5b1c3&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}";
        var buffer = NameBuffer(header, name, extra: 32);

        Assert.Equal(name, UsbIoParsers.ParseVariableName(buffer, header, out int actual));
        Assert.Equal(buffer.Length, actual);
    }

    [Fact]
    public void Reports_required_length_when_name_buffer_is_too_small()
    {
        var buffer = NameBuffer(8, "{36fc9e60-c465-11cf-8056-444553540000}\\0012", actualLength: 4096);

        UsbIoParsers.ParseVariableName(buffer, 8, out int actual);

        Assert.Equal(4096, actual);
    }

    [Fact]
    public void Decodes_hub_node_information()
    {
        var buffer = new byte[76];
        buffer[6] = 4;   // bNumberOfPorts
        buffer[75] = 1;  // HubIsBusPowered

        Assert.Equal(new UsbIoParsers.NodeInformation(4, true), UsbIoParsers.ParseNodeInformation(buffer));

        BinaryPrimitives.WriteUInt32LittleEndian(buffer, 1); // UsbMIParent, not a hub
        Assert.Null(UsbIoParsers.ParseNodeInformation(buffer));
    }

    [Theory]
    [InlineData(1, UsbHubType.RootHub)]
    [InlineData(2, UsbHubType.Usb20Hub)]
    [InlineData(3, UsbHubType.Usb30Hub)]
    [InlineData(9, UsbHubType.Unknown)]
    public void Decodes_hub_information_ex(int rawType, UsbHubType expected)
    {
        var buffer = new byte[77];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, (uint)rawType);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), 25);

        Assert.Equal(new UsbIoParsers.HubInformationEx(expected, 25), UsbIoParsers.ParseHubInformationEx(buffer));
    }

    [Fact]
    public void Decodes_connection_information_ex_v2()
    {
        var buffer = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 0b111);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(12), 0b0011);

        Assert.Equal(new UsbIoParsers.ConnectionInfoV2(0b111, 0b0011), UsbIoParsers.ParseConnectionInfoV2(buffer));
    }

    [Fact]
    public void Decodes_port_connector_properties_with_companion_hub()
    {
        const string companion = @"\\?\USB#ROOT_HUB30#4&2a5b1c3&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}";
        var name = Encoding.Unicode.GetBytes(companion + "\0");
        var buffer = new byte[16 + name.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), (uint)buffer.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 0b1001); // user-connectable + Type-C
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(14), 18);
        name.CopyTo(buffer, 16);

        var props = UsbIoParsers.ParseConnectorProperties(buffer)!.Value;

        Assert.Equal(0b1001u, props.Flags);
        Assert.Equal(18, props.CompanionPort);
        Assert.Equal(companion, props.CompanionHub);
    }

    [Fact]
    public void Builds_get_string_descriptor_request()
    {
        var request = UsbIoParsers.BuildDescriptorRequest(port: 5, descriptorType: 0x03, index: 2, languageId: 0x0409, length: 255);

        Assert.Equal(UsbIoParsers.DescriptorRequestHeaderSize + 255, request.Length);
        Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(request));
        Assert.Equal(0x80, request[4]);
        Assert.Equal(0x06, request[5]);
        Assert.Equal(0x0302, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(6)));
        Assert.Equal(0x0409, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(8)));
        Assert.Equal(255, BinaryPrimitives.ReadUInt16LittleEndian(request.AsSpan(10)));
    }

    [Theory]
    [InlineData(@"USB#ROOT_HUB30#4&2a5b1c3&0&0#{f18a0e88-c30c-11d0-8815-00a0c906bed8}", @"USB\ROOT_HUB30\4&2a5b1c3&0&0")]
    [InlineData(@"\\?\USB#VID_05E3&PID_0610#5&1b2c&0&4#{f18a0e88-c30c-11d0-8815-00a0c906bed8}", @"USB\VID_05E3&PID_0610\5&1b2c&0&4")]
    [InlineData("not-a-symbolic-link", null)]
    public void Converts_symbolic_names_to_instance_ids(string symbolic, string? expected)
    {
        Assert.Equal(expected, PnpDeviceIndex.SymbolicNameToInstanceId(symbolic));
    }
}
