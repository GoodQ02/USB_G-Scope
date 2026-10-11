using UsbGScope.Core.Descriptors;

namespace UsbGScope.Core.Tests;

public class ConfigurationDescriptorTests
{
    [Fact]
    public void Parses_real_logitech_receiver_configuration()
    {
        var c = ConfigurationDescriptorParser.Parse(Fixture.Load("logitech-c548.configuration"))!;

        Assert.Equal(109, c.TotalLength);
        Assert.Equal(4, c.NumInterfaces);
        Assert.Equal(1, c.ConfigurationValue);
        Assert.False(c.SelfPowered);
        Assert.True(c.RemoteWakeup);
        Assert.Equal(98, c.MaxPowerMilliamps(superSpeed: false));
        Assert.False(c.IsTruncated);
        Assert.Empty(c.Associations);

        Assert.Equal(4, c.Interfaces.Count);
        Assert.All(c.Interfaces, i => Assert.Equal("HID", i.ClassName));
        Assert.All(c.Interfaces, i => Assert.Single(i.ExtraDescriptors, x => x.Type == 0x21)); // HID class descriptor

        // Interface 0 is a boot keyboard, interface 1 a boot mouse.
        Assert.Equal((1, 1), (c.Interfaces[0].InterfaceSubClass, c.Interfaces[0].InterfaceProtocol));
        Assert.Equal((1, 2), (c.Interfaces[1].InterfaceSubClass, c.Interfaces[1].InterfaceProtocol));

        var ep = Assert.Single(c.Interfaces[3].Endpoints);
        Assert.Equal(0x84, ep.Address);
        Assert.Equal(4, ep.Number);
        Assert.True(ep.IsIn);
        Assert.Equal(UsbTransferType.Interrupt, ep.TransferType);
        Assert.Equal(64, ep.PacketBytes);
        Assert.Equal(1, ep.Interval);
    }

    [Fact]
    public void Parses_real_stream_deck_in_and_out_endpoints()
    {
        var c = ConfigurationDescriptorParser.Parse(Fixture.Load("streamdeck-neo.configuration"))!;

        Assert.Equal(500, c.MaxPowerMilliamps(superSpeed: false));
        var iface = Assert.Single(c.Interfaces);
        Assert.Equal(2, iface.Endpoints.Count);

        var (inEp, outEp) = (iface.Endpoints[0], iface.Endpoints[1]);
        Assert.True(inEp.IsIn);
        Assert.Equal(512, inEp.PacketBytes);
        Assert.False(outEp.IsIn);
        Assert.Equal(2, outEp.Number);
        Assert.Equal(1024, outEp.PacketBytes);
    }

    [Fact]
    public void Parses_real_superspeed_webcam_with_iad_and_alternate_settings()
    {
        var c = ConfigurationDescriptorParser.Parse(Fixture.Load("dell-webcam-wb7022.configuration"))!;

        Assert.Equal(902, c.TotalLength);
        Assert.False(c.IsTruncated);
        Assert.Equal(4, c.NumInterfaces);
        Assert.Equal(896, c.MaxPowerMilliamps(superSpeed: true)); // 112 * 8 mA

        var iad = Assert.Single(c.Associations);
        Assert.Equal((0, 3, "Video"), (iad.FirstInterface, iad.InterfaceCount, iad.ClassName));

        // Interfaces 1 and 2 each have a zero-bandwidth alt 0 and a streaming alt 1.
        Assert.Equal(
            new[] { "0.0", "1.0", "1.1", "2.0", "2.1", "3.0" },
            c.Interfaces.Select(i => $"{i.InterfaceNumber}.{i.AlternateSetting}"));
        Assert.Empty(c.Interfaces[1].Endpoints);

        var stream = Assert.Single(c.Interfaces[2].Endpoints);
        Assert.Equal(UsbTransferType.Isochronous, stream.TransferType);
        Assert.Equal(1024, stream.PacketBytes);

        // SuperSpeed endpoint companion descriptors (0x30) are kept, not dropped.
        Assert.Contains(c.Interfaces[2].ExtraDescriptors, x => x.Type == 0x30);
        Assert.Equal("HID", c.Interfaces[5].ClassName);
    }

    [Fact]
    public void Rejects_non_configuration_input()
    {
        Assert.Null(ConfigurationDescriptorParser.Parse(Fixture.Load("logitech-c548.device")));
        Assert.Null(ConfigurationDescriptorParser.Parse([0x09, 0x02, 0x09]));
    }

    [Fact]
    public void Header_only_buffer_is_marked_truncated()
    {
        var c = ConfigurationDescriptorParser.Parse(Fixture.Load("logitech-c548.configuration")[..9])!;

        Assert.True(c.IsTruncated);
        Assert.Empty(c.Interfaces);
        Assert.Equal(98, c.MaxPowerMilliamps(superSpeed: false));
    }

    [Fact]
    public void Zero_length_descriptor_stops_parsing_without_looping()
    {
        var data = Fixture.Load("streamdeck-neo.configuration");
        data[18] = 0x00; // bLength of the HID descriptor that follows interface 0

        var c = ConfigurationDescriptorParser.Parse(data)!;

        Assert.True(c.IsTruncated);
        Assert.Empty(Assert.Single(c.Interfaces).Endpoints);
    }

    [Fact]
    public void Descriptor_overrunning_total_length_is_marked_truncated()
    {
        var data = Fixture.Load("streamdeck-neo.configuration");
        data[34] = 0x20; // last endpoint claims 32 bytes, past wTotalLength

        var c = ConfigurationDescriptorParser.Parse(data)!;

        Assert.True(c.IsTruncated);
        Assert.Single(Assert.Single(c.Interfaces).Endpoints);
    }

    [Fact]
    public void Endpoint_before_any_interface_is_kept_as_configuration_extra()
    {
        byte[] data = [0x09, 0x02, 0x10, 0x00, 0x00, 0x01, 0x00, 0x80, 0x32, 0x07, 0x05, 0x81, 0x03, 0x08, 0x00, 0x0A];

        var c = ConfigurationDescriptorParser.Parse(data)!;

        Assert.Empty(c.Interfaces);
        Assert.Equal(0x05, Assert.Single(c.ConfigurationExtras).Type);
    }
}
