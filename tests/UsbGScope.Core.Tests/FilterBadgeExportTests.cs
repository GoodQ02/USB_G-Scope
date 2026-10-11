using System.Text.Json;
using UsbGScope.Core.Export;
using UsbGScope.Core.Model;
using UsbGScope.Core.Presentation;

namespace UsbGScope.Core.Tests;

/// <summary>Milestone 5: search / issues filter, badges, JSON export.</summary>
public class FilterBadgeExportTests
{
    private static UsbNode Device(int port, ushort vid, ushort pid, string name, string? serial = null, UsbPortInfo? portInfo = null) =>
        new(UsbNodeKind.Device, name, [])
        {
            PortNumber = port,
            Speed = UsbSpeed.High,
            Device = new UsbDeviceInfo(vid, pid, "2.00", "1.00", 0, 0, 0, 1, port, 1) { Manufacturer = "Acme", SerialNumber = serial },
            Pnp = new PnpDeviceInfo($@"USB\VID_{vid:X4}&PID_{pid:X4}\{serial ?? "5&1"}", name, null, "usbccgp", null, null),
            Port = portInfo,
        };

    // c0 / c0/0 (root) / c0/0/1 hub -> c0/0/1/2 receiver ; c0/0/3 webcam ; c0/0/4 broken
    private static UsbSnapshot Sample()
    {
        var hub = new UsbNode(UsbNodeKind.Hub, "Generic USB Hub", [Device(2, 0x046D, 0xC548, "USB Receiver")])
        {
            PortNumber = 1,
            Device = new UsbDeviceInfo(0x05E3, 0x0610, "2.10", "1.00", 9, 0, 1, 1, 1, 1),
        };
        var broken = Device(4, 0x1234, 0x5678, "Flaky Gadget") with { Pnp = new PnpDeviceInfo(@"USB\X\1", "Flaky Gadget", null, null, null, 43) };
        var root = new UsbNode(UsbNodeKind.RootHub, "USB Root Hub", [hub, Device(3, 0x413C, 0xC015, "Dell Webcam WB7022", serial: "SN123456"), broken]);
        return new UsbSnapshot(DateTimeOffset.UnixEpoch, [new UsbNode(UsbNodeKind.HostController, "xHCI", [root])]);
    }

    [Fact]
    public void No_filter_means_show_everything()
    {
        Assert.Null(NodeFilter.VisibleKeys(Sample(), "   ", issuesOnly: false));
        Assert.Empty(NodeFilter.Tokenize(null));
    }

    [Fact]
    public void Search_keeps_matches_and_their_ancestors_only()
    {
        var keys = NodeFilter.VisibleKeys(Sample(), "receiver", issuesOnly: false)!;
        Assert.Equal(["c0", "c0/0", "c0/0/1", "c0/0/1/2"], keys.Order());
    }

    [Fact]
    public void Search_matches_vid_pid_in_either_spelling_and_all_tokens()
    {
        Assert.Contains("c0/0/3", NodeFilter.VisibleKeys(Sample(), "413c:c015", false)!);
        Assert.Contains("c0/0/3", NodeFilter.VisibleKeys(Sample(), "vid_413c", false)!);
        Assert.Contains("c0/0/3", NodeFilter.VisibleKeys(Sample(), "dell webcam", false)!);
        Assert.DoesNotContain("c0/0/3", NodeFilter.VisibleKeys(Sample(), "dell receiver", false)!);
        Assert.Empty(NodeFilter.VisibleKeys(Sample(), "nothing-matches-this", false)!);
    }

    [Fact]
    public void Issues_only_shows_problem_devices_and_combines_with_search()
    {
        var keys = NodeFilter.VisibleKeys(Sample(), null, issuesOnly: true)!;
        Assert.Equal(["c0", "c0/0", "c0/0/4"], keys.Order());
        Assert.Empty(NodeFilter.VisibleKeys(Sample(), "webcam", issuesOnly: true)!);
    }

    [Fact]
    public void Search_finds_drive_letters_and_com_ports()
    {
        var node = Device(1, 1, 1, "Stick") with
        {
            Resources = new UsbDeviceResources([], [new UsbDrive("E:", "BACKUP", "exFAT", 1)], ["COM7"]),
        };
        Assert.True(NodeFilter.Matches(node, ["e:"], false));
        Assert.True(NodeFilter.Matches(node, ["com7"], false));
        Assert.True(NodeFilter.Matches(node, ["backup"], false));
    }

    [Fact]
    public void Type_c_badge_only_on_connected_devices()
    {
        var typeC = new UsbPortInfo(false, true, true, true, false, true, null, null);
        Assert.Equal(["Type-C"], NodePresenter.Badges(Device(1, 1, 1, "Phone", portInfo: typeC)));
        var empty = new UsbNode(UsbNodeKind.EmptyPort, "Port 1", []) { Port = typeC };
        Assert.Empty(NodePresenter.Badges(empty));
    }

    [Fact]
    public void Json_export_round_trips_and_masks_serials_on_request()
    {
        var snapshot = Sample();

        string plain = SnapshotJson.Serialize(snapshot);
        Assert.Contains("SN123456", plain);
        Assert.Contains("\"Kind\": \"HostController\"", plain); // enums as strings
        Assert.DoesNotContain("\"Error\": null", plain);        // nulls omitted

        string masked = SnapshotJson.Serialize(snapshot, maskSerials: true);
        Assert.DoesNotContain("SN123456", masked);
        using var doc = JsonDocument.Parse(masked);
        Assert.Equal(1, doc.RootElement.GetProperty("Controllers").GetArrayLength());
    }

    [Fact]
    public void Suggested_file_name_uses_capture_time()
    {
        var snapshot = new UsbSnapshot(new DateTimeOffset(2026, 10, 11, 17, 14, 5, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 11))), []);
        Assert.Equal("usb-g-scope-2026-10-11-171405", SnapshotJson.SuggestedFileName(snapshot));
    }
}
