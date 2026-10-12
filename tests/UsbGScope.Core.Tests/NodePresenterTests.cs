using UsbGScope.Core.Descriptors;
using UsbGScope.Core.Model;
using UsbGScope.Core.Presentation;

namespace UsbGScope.Core.Tests;

public class NodePresenterTests
{
    private static UsbNode DeviceFromFixture(string fixture, UsbSpeed speed = UsbSpeed.Full, string? service = null)
    {
        var dd = UsbDescriptorParser.ParseDevice(Fixture.Load(fixture + ".device"))!.Value;
        var config = ConfigurationDescriptorParser.Parse(Fixture.Load(fixture + ".configuration"));
        return new UsbNode(UsbNodeKind.Device, "Test device", [])
        {
            PortNumber = 3,
            Speed = speed,
            Device = new UsbDeviceInfo(dd.VendorId, dd.ProductId, "2.00", "1.00", dd.DeviceClass, dd.DeviceSubClass,
                dd.DeviceProtocol, dd.NumConfigurations, 4, 2) { Configuration = config },
            Pnp = service is null ? null : new PnpDeviceInfo(@"USB\X", "Desc", null, service, null, null),
        };
    }

    [Fact]
    public void Categorizes_real_devices_by_their_interfaces()
    {
        Assert.Equal(DeviceCategory.Keyboard, NodePresenter.Categorize(DeviceFromFixture("logitech-c548")));   // boot keyboard first
        Assert.Equal(DeviceCategory.Hid, NodePresenter.Categorize(DeviceFromFixture("streamdeck-neo")));       // non-boot HID
        Assert.Equal(DeviceCategory.Video, NodePresenter.Categorize(DeviceFromFixture("dell-webcam-wb7022"))); // IAD composite
    }

    [Fact]
    public void Bluetooth_service_wins_over_interface_class()
    {
        Assert.Equal(DeviceCategory.Bluetooth, NodePresenter.Categorize(DeviceFromFixture("logitech-c548", service: "BTHUSB")));
    }

    [Fact]
    public void Details_include_identity_configuration_and_windows_sections()
    {
        var sections = NodePresenter.BuildDetails(DeviceFromFixture("dell-webcam-wb7022", UsbSpeed.SuperSpeed, "usbccgp"));

        Assert.Equal(new[] { "Overview", "Device", "Configuration", "Windows" }, sections.Select(s => s.Title));
        Assert.Contains(sections[1].Rows, r => r is { Label: "Vendor / Product ID", Value: "413C:C015" });
        Assert.Contains(sections[2].Rows, r => r.Label == "Function 0–2" && r.Value == "Video");
        Assert.Contains(sections[2].Rows, r => r.Label == "Interface 1.1" && r.Value.Contains("EP1 IN · isochronous · 1024 bytes"));
    }

    [Fact]
    public void Warning_prefers_error_then_problem_code_then_downgrade()
    {
        var downgraded = new UsbNode(UsbNodeKind.Device, "x", []) { Speed = UsbSpeed.High, SuperSpeedCapable = true };
        var problem = downgraded with { Pnp = new PnpDeviceInfo("id", null, null, null, null, 28) };
        var error = problem with { Error = "DeviceFailedEnumeration" };

        Assert.Contains("USB 2.0 speed", NodePresenter.Warning(downgraded));
        Assert.Contains("code 28", NodePresenter.Warning(problem));
        Assert.Equal("DeviceFailedEnumeration", NodePresenter.Warning(error));
        Assert.Null(NodePresenter.Warning(new UsbNode(UsbNodeKind.Device, "ok", []) { Speed = UsbSpeed.High }));
    }

    [Fact]
    public void Hex_dump_wraps_at_16_bytes_with_offsets()
    {
        var bytes = Enumerable.Range(0, 18).Select(i => (byte)i).ToArray();

        Assert.Equal(
            "0000  00 01 02 03 04 05 06 07 08 09 0A 0B 0C 0D 0E 0F\n0010  10 11",
            NodePresenter.HexDump(bytes));
        Assert.Equal("", NodePresenter.HexDump(null));
    }

    [Fact]
    public void Clipboard_text_contains_sections_and_raw_descriptors()
    {
        var node = DeviceFromFixture("streamdeck-neo");
        node = node with { Device = node.Device! with { RawDeviceDescriptor = Fixture.Load("streamdeck-neo.device") } };

        string text = NodePresenter.ToText(node, NodePresenter.BuildDetails(node));

        Assert.Contains("[Device]", text);
        Assert.Contains("Vendor / Product ID: 0FD9:009A", text);
        Assert.Contains("[Device descriptor]\n0000  12 01 00 02", text.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Empty_port_subtitle_lists_capabilities()
    {
        var port = new UsbNode(UsbNodeKind.EmptyPort, "Port 2", [])
        {
            Port = new UsbPortInfo(true, true, true, true, false, true, null, null),
        };

        Assert.Equal("USB 3 · USB 2 · Type-C", NodePresenter.Subtitle(port));
    }

    [Theory]
    [InlineData("USB Condenser Microphone", DeviceCategory.Microphone)]
    [InlineData("Wireless Gaming Headset", DeviceCategory.Headset)]
    [InlineData("8BitDo Ultimate Wireless Gamepad", DeviceCategory.GameController)]
    [InlineData("USB-C Portable Display", DeviceCategory.Display)]
    public void Categorizes_specialized_devices_by_hints(string displayName, DeviceCategory expected)
    {
        var node = new UsbNode(UsbNodeKind.Device, displayName, [])
        {
            Device = new UsbDeviceInfo(0x1234, 0x5678, "2.00", "1.00", 0, 0, 0, 1, 0, 0),
        };
        Assert.Equal(expected, NodePresenter.Categorize(node));
    }
}
