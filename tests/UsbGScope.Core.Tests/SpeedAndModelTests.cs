using UsbGScope.Core.Enumeration;
using UsbGScope.Core.Model;
using static UsbGScope.Core.Enumeration.UsbSpeedResolver;

namespace UsbGScope.Core.Tests;

public class SpeedAndModelTests
{
    [Theory]
    [InlineData(0, null, UsbSpeed.Low)]
    [InlineData(1, null, UsbSpeed.Full)]
    [InlineData(2, null, UsbSpeed.High)]
    [InlineData(3, null, UsbSpeed.SuperSpeed)]
    [InlineData(7, null, UsbSpeed.Unknown)]
    [InlineData(2, SuperSpeedCapableOrHigher, UsbSpeed.High)]                                   // capable but on USB 2
    [InlineData(2, OperatingAtSuperSpeedOrHigher, UsbSpeed.SuperSpeed)]                         // V2 overrides legacy byte
    [InlineData(3, OperatingAtSuperSpeedOrHigher | OperatingAtSuperSpeedPlusOrHigher, UsbSpeed.SuperSpeedPlus)]
    public void Resolves_speed_from_legacy_code_and_v2_flags(int code, uint? flags, UsbSpeed expected)
    {
        Assert.Equal(expected, UsbSpeedResolver.Resolve((byte)code, flags));
    }

    private static UsbNode Node(UsbNodeKind kind, UsbSpeed speed, bool? ssCapable) =>
        new(kind, "x", []) { Speed = speed, SuperSpeedCapable = ssCapable };

    [Fact]
    public void Flags_superspeed_device_running_at_high_speed()
    {
        Assert.True(Node(UsbNodeKind.Device, UsbSpeed.High, true).IsSpeedDowngraded);
    }

    [Theory]
    [InlineData(UsbNodeKind.Device, UsbSpeed.SuperSpeed, true)]
    [InlineData(UsbNodeKind.Device, UsbSpeed.High, false)]
    [InlineData(UsbNodeKind.Device, UsbSpeed.High, null)]
    [InlineData(UsbNodeKind.Hub, UsbSpeed.High, true)] // USB 2 half of a USB 3 hub is normal
    public void Does_not_flag_normal_configurations(UsbNodeKind kind, UsbSpeed speed, bool? ssCapable)
    {
        Assert.False(Node(kind, speed, ssCapable).IsSpeedDowngraded);
    }

    [Fact]
    public void Mask_serials_hides_serial_and_serial_bearing_instance_id()
    {
        var device = new UsbNode(UsbNodeKind.Device, "Stream Deck", [])
        {
            Device = new UsbDeviceInfo(0x0FD9, 0x0080, "2.00", "4.40", 0, 0, 0, 1, 5, 2) { SerialNumber = "CL12K1A00042" },
            Pnp = new PnpDeviceInfo(@"USB\VID_0FD9&PID_0080\CL12K1A00042", "USB Input Device", null, null, null, null),
        };
        var hub = new UsbNode(UsbNodeKind.RootHub, "Root", [device]);
        var snapshot = new UsbSnapshot(DateTimeOffset.UnixEpoch, [new UsbNode(UsbNodeKind.HostController, "xHCI", [hub])]);

        var masked = snapshot.MaskSerials().Flatten().Single(n => n.Kind == UsbNodeKind.Device);

        Assert.Equal("<masked>", masked.Device!.SerialNumber);
        Assert.Equal(@"USB\VID_0FD9&PID_0080\<masked>", masked.Pnp!.InstanceId);
        Assert.Equal("CL12K1A00042", device.Device!.SerialNumber); // original untouched
    }

    [Fact]
    public void Flatten_visits_nodes_depth_first_in_order()
    {
        var tree = new UsbSnapshot(DateTimeOffset.UnixEpoch,
        [
            new UsbNode(UsbNodeKind.HostController, "A",
            [
                new UsbNode(UsbNodeKind.RootHub, "A1", [new UsbNode(UsbNodeKind.Device, "A1a", [])]),
            ]),
            new UsbNode(UsbNodeKind.HostController, "B", []),
        ]);

        Assert.Equal(new[] { "A", "A1", "A1a", "B" }, tree.Flatten().Select(n => n.DisplayName));
    }
}
