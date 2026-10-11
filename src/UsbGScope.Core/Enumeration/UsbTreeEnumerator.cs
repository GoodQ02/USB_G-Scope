// Enumeration strategy (controllers -> root hub -> ports -> downstream hubs) follows
// Microsoft's USBView sample: https://github.com/microsoft/Windows-driver-samples/tree/main/usb/usbview (MS-PL)
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using UsbGScope.Core.Descriptors;
using UsbGScope.Core.Interop;
using UsbGScope.Core.Model;

namespace UsbGScope.Core.Enumeration;

/// <summary>Captures a read-only snapshot of the USB topology. No elevation required.</summary>
public static class UsbTreeEnumerator
{
    /// <summary>GUID_DEVINTERFACE_USB_HOST_CONTROLLER.</summary>
    private static readonly Guid HostControllerInterface = new("3ABF6F2D-71C4-462A-8A92-1E6861E6AF27");

    /// <summary>USB allows 5 tiers of external hubs; this guards against malformed loops.</summary>
    private const int MaxHubDepth = 8;

    private const ushort DefaultLanguageId = 0x0409;

    public static UsbSnapshot Capture()
    {
        var pnp = PnpDeviceIndex.Build();
        var controllers = SetupApi.GetInterfaces(HostControllerInterface)
            .Select(entry => BuildController(entry, pnp))
            .ToList();
        return new UsbSnapshot(DateTimeOffset.Now, controllers);
    }

    private static UsbNode BuildController(SetupApi.InterfaceEntry entry, PnpDeviceIndex pnp)
    {
        var info = entry.InstanceId is null ? null : pnp.FindByInstanceId(entry.InstanceId);
        string name = info?.Description ?? "USB host controller";

        using SafeFileHandle controller = UsbIo.Open(entry.DevicePath);
        if (controller.IsInvalid)
            return new UsbNode(UsbNodeKind.HostController, name, []) { Pnp = info, Error = Win32Message(UsbIo.LastError) };

        string? rootName = UsbIo.GetRootHubName(controller);
        if (rootName is null)
            return new UsbNode(UsbNodeKind.HostController, name, []) { Pnp = info, Error = "Root hub name unavailable: " + Win32Message(UsbIo.LastError) };

        var ports = EnumerateHub(rootName, depth: 0, pnp, out var hubInfo, out var error);
        var rootPnp = pnp.FindBySymbolicName(rootName);
        var root = new UsbNode(UsbNodeKind.RootHub, rootPnp?.Description ?? "USB Root Hub", ports)
        {
            Hub = hubInfo,
            Pnp = rootPnp,
            Error = error,
        };

        return new UsbNode(UsbNodeKind.HostController, name, [root]) { Pnp = info };
    }

    private static List<UsbNode> EnumerateHub(string hubName, int depth, PnpDeviceIndex pnp, out UsbHubInfo? hubInfo, out string? error)
    {
        hubInfo = null;
        error = null;

        using SafeFileHandle hub = UsbIo.Open(@"\\.\" + hubName);
        if (hub.IsInvalid)
        {
            error = "Could not open hub: " + Win32Message(UsbIo.LastError);
            return [];
        }

        hubInfo = UsbIo.GetHubInfo(hub, hubName);
        if (hubInfo is null)
        {
            error = "Hub information unavailable: " + Win32Message(UsbIo.LastError);
            return [];
        }

        var ports = new List<UsbNode>(hubInfo.PortCount);
        for (uint port = 1; port <= hubInfo.PortCount; port++)
            ports.Add(BuildPort(hub, port, depth, pnp));
        return ports;
    }

    private static UsbNode BuildPort(SafeFileHandle hub, uint port, int depth, PnpDeviceIndex pnp)
    {
        var connection = UsbIo.GetConnectionInfoEx(hub, port);
        var v2 = UsbIo.GetConnectionInfoV2(hub, port);
        var connector = UsbIo.GetPortConnectorProperties(hub, port);

        var portInfo = new UsbPortInfo(
            SupportsUsb1: v2 is { } a ? (a.SupportedProtocols & 0b001) != 0 : null,
            SupportsUsb2: v2 is { } b ? (b.SupportedProtocols & 0b010) != 0 : null,
            SupportsUsb3: v2 is { } c ? (c.SupportedProtocols & 0b100) != 0 : null,
            UserConnectable: connector is { } d ? (d.Flags & 0b0001) != 0 : null,
            DebugCapable: connector is { } e ? (e.Flags & 0b0010) != 0 : null,
            TypeC: connector is { } f ? (f.Flags & 0b1000) != 0 : null,
            CompanionPortNumber: connector is { CompanionPort: > 0 } g ? g.CompanionPort : null,
            CompanionHubName: connector?.CompanionHub);

        if (connection is not { } conn)
        {
            return new UsbNode(UsbNodeKind.EmptyPort, $"Port {port}", [])
            {
                PortNumber = (int)port,
                Port = portInfo,
                Error = "Port query failed: " + Win32Message(UsbIo.LastError),
            };
        }

        if (conn.Status == UsbConnectionStatus.NoDeviceConnected)
        {
            return new UsbNode(UsbNodeKind.EmptyPort, $"Port {port}", [])
            {
                PortNumber = (int)port,
                Port = portInfo,
                ConnectionStatus = conn.Status,
            };
        }

        uint? flags = v2?.Flags;
        var speed = UsbSpeedResolver.Resolve(conn.SpeedCode, flags);

        string? driverKey = UsbIo.GetDriverKeyName(hub, port);
        var pnpInfo = driverKey is null ? null : pnp.FindByDriverKey(driverKey);

        var descriptor = UsbDescriptorParser.ParseDevice(conn.DeviceDescriptor);
        UsbDeviceInfo? deviceInfo = null;
        if (descriptor is { } dd && dd.VendorId != 0)
        {
            deviceInfo = new UsbDeviceInfo(
                dd.VendorId, dd.ProductId,
                UsbDescriptorParser.FormatBcd(dd.BcdUsb), UsbDescriptorParser.FormatBcd(dd.BcdDevice),
                dd.DeviceClass, dd.DeviceSubClass, dd.DeviceProtocol, dd.NumConfigurations,
                conn.DeviceAddress, (int)conn.OpenPipes)
            {
                RawDeviceDescriptor = conn.DeviceDescriptor,
            };

            if (conn.Status == UsbConnectionStatus.DeviceConnected)
                deviceInfo = AddDescriptorDetails(hub, port, dd, deviceInfo, speed >= UsbSpeed.SuperSpeed);
        }

        List<UsbNode> children = [];
        UsbHubInfo? childHub = null;
        string? error = conn.Status == UsbConnectionStatus.DeviceConnected ? null : conn.Status.ToString();
        if (conn.IsHub && conn.Status == UsbConnectionStatus.DeviceConnected)
        {
            if (depth >= MaxHubDepth)
                error = "Hub nesting limit reached";
            else if (UsbIo.GetNodeConnectionName(hub, port) is { } childName)
                children = EnumerateHub(childName, depth + 1, pnp, out childHub, out error);
            else
                error = "Downstream hub name unavailable";
        }

        string displayName = deviceInfo?.Product
            ?? pnpInfo?.Description
            ?? (deviceInfo is not null ? $"USB device {deviceInfo.VendorId:X4}:{deviceInfo.ProductId:X4}" : conn.Status.ToString());

        return new UsbNode(conn.IsHub ? UsbNodeKind.Hub : UsbNodeKind.Device, displayName, children)
        {
            PortNumber = (int)port,
            ConnectionStatus = conn.Status,
            Speed = speed,
            SuperSpeedCapable = flags is uint f1 ? (f1 & UsbSpeedResolver.SuperSpeedCapableOrHigher) != 0 : null,
            SuperSpeedPlusCapable = flags is uint f2 ? (f2 & UsbSpeedResolver.SuperSpeedPlusCapableOrHigher) != 0 : null,
            Port = portInfo,
            Device = deviceInfo,
            Hub = childHub,
            Pnp = pnpInfo,
            Error = error,
        };
    }

    /// <summary>Adds string descriptors and the decoded configuration descriptor set.</summary>
    private static UsbDeviceInfo AddDescriptorDetails(SafeFileHandle hub, uint port, DeviceDescriptor dd, UsbDeviceInfo info, bool superSpeed)
    {
        bool hasStrings = dd.ManufacturerIndex != 0 || dd.ProductIndex != 0 || dd.SerialNumberIndex != 0;
        ushort language = DefaultLanguageId;
        if (hasStrings && UsbIo.GetDescriptor(hub, port, UsbDescriptorParser.StringType, 0, 0, 255) is { } langs)
            language = UsbDescriptorParser.ParseFirstLanguageId(langs) ?? DefaultLanguageId;

        string? ReadString(byte index) =>
            index == 0 ? null
            : UsbIo.GetDescriptor(hub, port, UsbDescriptorParser.StringType, index, language, 255) is { } bytes
                ? UsbDescriptorParser.ParseString(bytes)
                : null;

        byte[]? rawConfig = UsbIo.GetConfigurationDescriptor(hub, port);
        var config = rawConfig is null ? null : ConfigurationDescriptorParser.Parse(rawConfig);

        return info with
        {
            Manufacturer = ReadString(dd.ManufacturerIndex),
            Product = ReadString(dd.ProductIndex),
            SerialNumber = ReadString(dd.SerialNumberIndex),
            MaxPowerMilliamps = config?.MaxPowerMilliamps(superSpeed),
            Configuration = config,
            RawConfigurationDescriptor = rawConfig,
        };
    }
    private static string Win32Message(int error) => $"{new Win32Exception(error).Message} (0x{error:X})";
}
