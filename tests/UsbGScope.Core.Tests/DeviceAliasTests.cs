using System.Text.Json;
using UsbGScope.Core.Aliases;
using UsbGScope.Core.Export;
using UsbGScope.Core.Model;
using UsbGScope.Core.Presentation;

namespace UsbGScope.Core.Tests;

public class DeviceAliasTests
{
    private static UsbNode CreateDevice(int port, ushort vid, ushort pid, string name, string? serial = null, string? instanceId = null) =>
        new(UsbNodeKind.Device, name, [])
        {
            PortNumber = port,
            Speed = UsbSpeed.High,
            Device = new UsbDeviceInfo(vid, pid, "2.00", "1.00", 0, 0, 0, 1, port, 1)
            {
                Manufacturer = "TestCorp",
                Product = name,
                SerialNumber = serial,
            },
            Pnp = new PnpDeviceInfo(instanceId ?? $@"USB\VID_{vid:X4}&PID_{pid:X4}\{serial ?? "6&1234&0&1"}", name, null, "usbccgp", null, null),
        };

    [Fact]
    public void DeviceAliasKeys_generates_serial_key_for_device_with_serial()
    {
        var node = CreateDevice(1, 0x046D, 0xC52B, "Logitech Receiver", serial: "SN12345678");
        Assert.Equal("sn:046D:C52B:SN12345678", DeviceAliasKeys.GetDeviceSerialKey(node));
        Assert.Equal("sn:046D:C52B:SN12345678", DeviceAliasKeys.GetPrimarySaveKey(node, "c0/0/1"));
    }

    [Fact]
    public void DeviceAliasKeys_ignores_generic_placeholder_serials()
    {
        var zeroNode = CreateDevice(1, 0x046D, 0xC52B, "Generic Device", serial: "00000000");
        Assert.Null(DeviceAliasKeys.GetDeviceSerialKey(zeroNode));

        var oneNode = CreateDevice(1, 0x046D, 0xC52B, "Generic Device", serial: "0001");
        Assert.Null(DeviceAliasKeys.GetDeviceSerialKey(oneNode));
    }

    [Fact]
    public void DeviceAliasKeys_generates_instance_id_when_no_serial()
    {
        var node = CreateDevice(2, 0x1234, 0x5678, "Mouse", serial: null, instanceId: @"USB\VID_1234&PID_5678\6&2A7E2F8B&0&2");
        Assert.Null(DeviceAliasKeys.GetDeviceSerialKey(node));
        Assert.Equal(@"id:USB\VID_1234&PID_5678\6&2A7E2F8B&0&2", DeviceAliasKeys.GetInstanceIdKey(node));
        Assert.Equal(@"id:USB\VID_1234&PID_5678\6&2A7E2F8B&0&2", DeviceAliasKeys.GetPrimarySaveKey(node, "c0/0/2"));
    }

    [Fact]
    public void DeviceAliasStore_saves_and_loads_from_disk()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"usbgscope-test-aliases-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DeviceAliasStore(tempFile);
            store.SetAlias("sn:046D:C52B:123", "Work Mouse");
            store.SetAlias("id:USB\\VID_1111&PID_2222\\1", "Desk Cam");

            Assert.True(File.Exists(tempFile));

            var reloaded = DeviceAliasStore.Load(tempFile);
            Assert.Equal("Work Mouse", reloaded.GetAliasByKey("sn:046D:C52B:123"));
            Assert.Equal("Desk Cam", reloaded.GetAliasByKey("id:USB\\VID_1111&PID_2222\\1"));

            reloaded.RemoveAlias("sn:046D:C52B:123");
            var secondReload = DeviceAliasStore.Load(tempFile);
            Assert.Null(secondReload.GetAliasByKey("sn:046D:C52B:123"));
            Assert.Equal("Desk Cam", secondReload.GetAliasByKey("id:USB\\VID_1111&PID_2222\\1"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void DeviceAliasStore_resolves_by_serial_even_if_device_moved_ports()
    {
        var store = new DeviceAliasStore(filePath: null);
        store.SetAlias("sn:046D:C548:PROD-XYZ", "Gaming Headset");

        // Plugged into Port 1
        var port1 = CreateDevice(1, 0x046D, 0xC548, "Headset", serial: "PROD-XYZ");
        Assert.Equal("Gaming Headset", store.GetAlias(port1, "c0/0/1"));

        // Moved to Port 4 on an external hub
        var port4 = CreateDevice(4, 0x046D, 0xC548, "Headset", serial: "PROD-XYZ", instanceId: @"USB\VID_046D&PID_C548\DIFFERENT_INSTANCE");
        Assert.Equal("Gaming Headset", store.GetAlias(port4, "c0/0/2/4"));
    }

    [Fact]
    public void UsbSnapshot_apply_aliases_enriches_nodes_and_preserves_hardware_info()
    {
        var store = new DeviceAliasStore(filePath: null);
        store.SetAlias("sn:413C:C015:DELL-SN", "Office Webcam");

        var dev = CreateDevice(1, 0x413C, 0xC015, "Dell Webcam WB7022", serial: "DELL-SN");
        var root = new UsbNode(UsbNodeKind.RootHub, "Root Hub", [dev]);
        var snapshot = new UsbSnapshot(DateTimeOffset.UtcNow, [new UsbNode(UsbNodeKind.HostController, "xHCI", [root])]);

        var enriched = snapshot.ApplyAliases(store);
        var enrichedDev = enriched.Controllers[0].Children[0].Children[0];

        Assert.Equal("Office Webcam", enrichedDev.CustomAlias);
        Assert.Equal("Dell Webcam WB7022", enrichedDev.DisplayName); // Hardware name intact
    }

    [Fact]
    public void NodePresenter_shows_nickname_and_hardware_name()
    {
        var node = CreateDevice(2, 0x413C, 0xC015, "Dell Webcam WB7022", serial: "DELL-SN") with
        {
            CustomAlias = "Conference Camera",
        };

        string sub = NodePresenter.Subtitle(node);
        Assert.Contains("Dell Webcam WB7022", sub);
        Assert.Contains("413C:C015", sub);

        var details = NodePresenter.BuildDetails(node);
        var overview = Assert.Single(details, d => d.Title == "Overview");
        Assert.Equal("Conference Camera", overview.Rows.First(r => r.Label == "Nickname").Value);
        Assert.Equal("Dell Webcam WB7022", overview.Rows.First(r => r.Label == "Hardware name").Value);
    }

    [Fact]
    public void NodeFilter_matches_custom_alias_tokens()
    {
        var dev = CreateDevice(1, 0x046D, 0xC52B, "Wireless Receiver", serial: "SN888") with
        {
            CustomAlias = "Studio Microphone",
        };
        var root = new UsbNode(UsbNodeKind.RootHub, "Root Hub", [dev]);
        var snapshot = new UsbSnapshot(DateTimeOffset.UtcNow, [new UsbNode(UsbNodeKind.HostController, "xHCI", [root])]);

        // Search by alias token
        var keys = NodeFilter.VisibleKeys(snapshot, "Studio", false);
        Assert.NotNull(keys);
        Assert.Contains("c0/0/1", keys);

        // Search by hardware token still works
        var hardwareKeys = NodeFilter.VisibleKeys(snapshot, "Wireless", false);
        Assert.NotNull(hardwareKeys);
        Assert.Contains("c0/0/1", hardwareKeys);
    }

    [Fact]
    public void SnapshotJson_serializes_custom_alias_when_present()
    {
        var dev = CreateDevice(1, 0x046D, 0xC52B, "Receiver", serial: "SN123") with
        {
            CustomAlias = "My Dongle",
        };
        var root = new UsbNode(UsbNodeKind.RootHub, "Root Hub", [dev]);
        var snapshot = new UsbSnapshot(DateTimeOffset.UtcNow, [new UsbNode(UsbNodeKind.HostController, "xHCI", [root])]);

        string json = SnapshotJson.Serialize(snapshot);
        Assert.Contains("\"CustomAlias\": \"My Dongle\"", json);

        var plainDev = CreateDevice(2, 0x046D, 0xC52B, "Receiver2", serial: "SN456");
        var plainSnapshot = new UsbSnapshot(DateTimeOffset.UtcNow, [new UsbNode(UsbNodeKind.HostController, "xHCI", [new UsbNode(UsbNodeKind.RootHub, "Root Hub", [plainDev])])]);
        string plainJson = SnapshotJson.Serialize(plainSnapshot);
        Assert.DoesNotContain("\"CustomAlias\"", plainJson);
    }

    [Fact]
    public void DeviceAliasStore_RemoveAliasForNode_clears_all_candidate_keys()
    {
        var store = new DeviceAliasStore(filePath: null);
        var node = CreateDevice(1, 0x046D, 0xC52B, "Wireless Receiver", serial: "SN888", instanceId: @"USB\VID_046D&PID_C52B\1");

        store.SetAlias(DeviceAliasKeys.GetDeviceSerialKey(node)!, "Alias By Serial");
        store.SetAlias(DeviceAliasKeys.GetInstanceIdKey(node)!, "Alias By Instance");
        Assert.NotNull(store.GetAlias(node, "c0/0/1"));

        store.RemoveAliasForNode(node, "c0/0/1");
        Assert.Null(store.GetAlias(node, "c0/0/1"));
        Assert.Null(store.GetAliasByKey("sn:046D:C52B:SN888"));
        Assert.Null(store.GetAliasByKey(@"id:USB\VID_046D&PID_C52B\1"));
    }
}
