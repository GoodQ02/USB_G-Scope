using UsbGScope.Core.Enumeration;
using UsbGScope.Core.Interop;
using UsbGScope.Core.Model;
using UsbGScope.Core.Presentation;

namespace UsbGScope.Core.Tests;

/// <summary>Milestone 4: device resources (drives, COM ports, child devices), hot-plug plumbing, diffing.</summary>
public class HotPlugAndResourceTests
{
    private static UsbNode Device(int port, ushort vid, ushort pid, UsbDeviceResources? resources = null, string? serial = null) =>
        new(UsbNodeKind.Device, $"Dev {vid:X4}", [])
        {
            PortNumber = port,
            Device = new UsbDeviceInfo(vid, pid, "2.00", "1.00", 0, 0, 0, 1, port, 1) { SerialNumber = serial },
            Pnp = new PnpDeviceInfo($@"USB\VID_{vid:X4}&PID_{pid:X4}\{serial ?? "5&1"}", "Desc", null, null, null, null),
            Resources = resources,
        };

    private static UsbSnapshot Snapshot(params UsbNode[] ports)
    {
        var root = new UsbNode(UsbNodeKind.RootHub, "Root", ports);
        return new UsbSnapshot(DateTimeOffset.UnixEpoch, [new UsbNode(UsbNodeKind.HostController, "xHCI", [root])]);
    }

    [Fact]
    public void Parses_storage_device_number_with_distinct_fields()
    {
        byte[] buffer = [0x07, 0, 0, 0, 0x02, 0, 0, 0, 0x01, 0, 0, 0];
        var n = StorageIo.ParseDeviceNumber(buffer)!.Value;
        Assert.Equal(7u, n.DeviceType);       // FILE_DEVICE_DISK
        Assert.Equal(2u, n.Number);
        Assert.Equal(1u, n.PartitionNumber);
        Assert.Equal((7u, 2u), n.DiskKey);
        Assert.Null(StorageIo.ParseDeviceNumber(buffer.AsSpan(0, 11)));
    }

    [Fact]
    public void Com_ports_sort_numerically()
    {
        string[] ports = ["COM10", "COM3", "COM2"];
        Assert.Equal(["COM2", "COM3", "COM10"], ports.Order(ComPortComparer.Instance));
    }

    [Fact]
    public void Only_arrival_and_removal_of_relevant_interfaces_trigger_refresh()
    {
        var usbDevice = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
        var volume = new Guid("53F5630D-B6BF-11D0-94F2-00A0C91EFB8B");
        var network = new Guid("CAC88484-7515-4C03-82E6-71A87ABAC361");

        Assert.True(UsbChangeWatcher.IsRelevant(0, usbDevice));
        Assert.True(UsbChangeWatcher.IsRelevant(1, volume));
        Assert.False(UsbChangeWatcher.IsRelevant(0, network));
        Assert.False(UsbChangeWatcher.IsRelevant(2, usbDevice)); // query-remove etc. are not changes
    }

    [Fact]
    public async Task Debouncer_coalesces_bursts_into_one_call()
    {
        int calls = 0;
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(80), () => Interlocked.Increment(ref calls));
        for (int i = 0; i < 10; i++)
        {
            debouncer.Trigger();
            await Task.Delay(10);
        }
        await Task.Delay(400);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Watcher_registers_and_disposes_without_admin()
    {
        using var watcher = new UsbChangeWatcher();
    }

    [Fact]
    public void Node_keys_use_port_numbers()
    {
        var keys = NodePaths.Enumerate(Snapshot(Device(4, 0x046D, 0xC548))).Select(e => e.Key).ToList();
        Assert.Equal(["c0", "c0/0", "c0/0/4"], keys);
    }

    [Fact]
    public void Diff_reports_arrivals_removals_and_swaps()
    {
        var before = Snapshot(Device(1, 0x1111, 0x0001), Device(2, 0x2222, 0x0002));
        var after = Snapshot(Device(1, 0x1111, 0x0001), Device(2, 0x3333, 0x0003), Device(3, 0x4444, 0x0004));

        var diff = NodePaths.Diff(before, after);
        Assert.Equal([0x3333, 0x4444], diff.Added.Select(n => (int)n.Device!.VendorId).Order());
        Assert.Equal(0x2222, Assert.Single(diff.Removed).Device!.VendorId);
        Assert.Equal(["c0/0/2", "c0/0/3"], diff.AddedKeys.Order());
        Assert.True(NodePaths.Diff(after, after).IsEmpty);
    }

    [Fact]
    public void Resources_appear_in_subtitle_details_and_warnings()
    {
        var resources = new UsbDeviceResources(
            [
                new PnpChildDevice(@"USBSTOR\DISK&VEN_X\ABC123&0", "SanDisk USB Device", "DiskDrive", null, 1),
                new PnpChildDevice(@"FTDIBUS\X\0000", "USB Serial Port (COM7)", "Ports", 28, 2),
            ],
            [new UsbDrive("E:", "BACKUP", "exFAT", 64_000_000_000)],
            ["COM7"]);
        var node = Device(2, 0x0781, 0x5581, resources);

        Assert.Equal("0781:5581 · Desc", NodePresenter.Subtitle(node));
        Assert.Equal(["E:", "COM7"], NodePresenter.Badges(node));

        var sections = NodePresenter.BuildDetails(node);
        var res = Assert.Single(sections, s => s.Title == "Resources");
        Assert.Contains(res.Rows, r => r.Label == "Drive E:" && r.Value == "BACKUP · exFAT · 64 GB");
        Assert.Contains(res.Rows, r => r.Label == "Serial port" && r.Value == "COM7");

        var children = Assert.Single(sections, s => s.Title == "Windows devices");
        Assert.Equal("DiskDrive", children.Rows[0].Label);
        Assert.Equal("  Ports", children.Rows[1].Label); // indented by depth
        Assert.EndsWith("problem code 28", children.Rows[1].Value);

        Assert.Contains("USB Serial Port (COM7)", NodePresenter.Warning(node));
    }

    [Fact]
    public void Drive_label_handles_empty_media()
    {
        Assert.Equal("No media", NodePresenter.DriveLabel(new UsbDrive("F:", null, null, null)));
        Assert.Equal("1.5 KB", NodePresenter.FormatBytes(1500));
        Assert.Equal("999 B", NodePresenter.FormatBytes(999));
    }

    [Fact]
    public void Masking_removes_serial_from_child_instance_ids()
    {
        var resources = new UsbDeviceResources([new PnpChildDevice(@"USBSTOR\DISK&VEN_X\ABC123&0", null, "DiskDrive", null, 1)], [], []);
        var masked = Snapshot(Device(1, 0x0781, 0x5581, resources, serial: "ABC123")).MaskSerials();

        var device = masked.Flatten().Single(n => n.Kind == UsbNodeKind.Device);
        Assert.Equal(@"USBSTOR\DISK&VEN_X\<masked>&0", device.Resources!.ChildDevices[0].InstanceId);
        Assert.DoesNotContain("ABC123", device.Pnp!.InstanceId);
    }
}
