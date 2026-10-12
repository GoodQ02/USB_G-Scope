using UsbGScope.Core.Aliases;
using UsbGScope.Core.Descriptors;
using UsbGScope.Core.Presentation;

namespace UsbGScope.Core.Model;

/// <summary>Kind of node in the USB topology tree.</summary>
public enum UsbNodeKind
{
    HostController,
    RootHub,
    Hub,
    Device,
    EmptyPort,
}

/// <summary>Negotiated bus speed reported by the hub driver.</summary>
public enum UsbSpeed
{
    Unknown,
    Low,            // 1.5 Mbps
    Full,           // 12 Mbps
    High,           // 480 Mbps
    SuperSpeed,     // 5 Gbps
    SuperSpeedPlus, // 10/20 Gbps
}

/// <summary>Mirrors USB_CONNECTION_STATUS from usbioctl.h.</summary>
public enum UsbConnectionStatus
{
    NoDeviceConnected = 0,
    DeviceConnected = 1,
    DeviceFailedEnumeration = 2,
    DeviceGeneralFailure = 3,
    DeviceCausedOvercurrent = 4,
    DeviceNotEnoughPower = 5,
    DeviceNotEnoughBandwidth = 6,
    DeviceHubNestedTooDeeply = 7,
    DeviceInLegacyHub = 8,
    DeviceEnumerating = 9,
    DeviceReset = 10,
}

/// <summary>Mirrors USB_HUB_TYPE from usbioctl.h.</summary>
public enum UsbHubType
{
    Unknown = 0,
    RootHub = 1,
    Usb20Hub = 2,
    Usb30Hub = 3,
}

/// <summary>Immutable snapshot of one node in the USB tree.</summary>
public sealed record UsbNode(
    UsbNodeKind Kind,
    string DisplayName,
    IReadOnlyList<UsbNode> Children)
{
    /// <summary>User-assigned custom friendly nickname, persisted locally.</summary>
    public string? CustomAlias { get; init; }

    /// <summary>1-based port number on the parent hub (null for controllers and root hubs).</summary>
    public int? PortNumber { get; init; }
    public UsbConnectionStatus? ConnectionStatus { get; init; }
    public UsbSpeed Speed { get; init; } = UsbSpeed.Unknown;
    public bool? SuperSpeedCapable { get; init; }
    public bool? SuperSpeedPlusCapable { get; init; }
    public UsbPortInfo? Port { get; init; }
    public UsbDeviceInfo? Device { get; init; }
    public UsbHubInfo? Hub { get; init; }
    public PnpDeviceInfo? Pnp { get; init; }
    /// <summary>Windows devices, drive letters and COM ports created for this device (Device nodes only).</summary>
    public UsbDeviceResources? Resources { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// True when a SuperSpeed-capable device is running at a USB 2.0 speed (cable, port, or hub limit).
    /// Hubs are excluded: a USB 3 hub always presents a separate USB 2.0 hub on the USB 2 bus.
    /// </summary>
    public bool IsSpeedDowngraded =>
        Kind == UsbNodeKind.Device
        && SuperSpeedCapable == true
        && Speed is UsbSpeed.Low or UsbSpeed.Full or UsbSpeed.High;
}

/// <summary>Capabilities of a hub port, independent of what is plugged in.</summary>
public sealed record UsbPortInfo(
    bool? SupportsUsb1,
    bool? SupportsUsb2,
    bool? SupportsUsb3,
    bool? UserConnectable,
    bool? DebugCapable,
    bool? TypeC,
    int? CompanionPortNumber,
    string? CompanionHubName);

/// <summary>Data from the device descriptor, string descriptors, and connection info.</summary>
public sealed record UsbDeviceInfo(
    ushort VendorId,
    ushort ProductId,
    string UsbVersion,
    string DeviceVersion,
    byte DeviceClass,
    byte DeviceSubClass,
    byte DeviceProtocol,
    byte NumConfigurations,
    int DeviceAddress,
    int OpenPipes)
{
    public string? Manufacturer { get; init; }
    public string? Product { get; init; }
    public string? SerialNumber { get; init; }
    public int? MaxPowerMilliamps { get; init; }
    public string ClassName => UsbClassNames.Get(DeviceClass);
    public ConfigurationDescriptor? Configuration { get; init; }
    public byte[]? RawDeviceDescriptor { get; init; }
    public byte[]? RawConfigurationDescriptor { get; init; }
}

public sealed record UsbHubInfo(UsbHubType Type, int PortCount, bool? BusPowered, string SymbolicName);

/// <summary>Plug and Play view of a device (Device Manager data).</summary>
public sealed record PnpDeviceInfo(
    string InstanceId,
    string? Description,
    string? DriverKey,
    string? Service,
    string? LocationInformation,
    uint? ProblemCode)
{
    /// <summary>Device setup class name (e.g. "USB", "Ports", "DiskDrive").</summary>
    public string? ClassName { get; init; }
}

/// <summary>A Windows device node below a USB device (interface function, disk, COM port...).</summary>
public sealed record PnpChildDevice(string InstanceId, string? Description, string? ClassName, uint? ProblemCode, int Depth);

/// <summary>A mounted volume that lives on a USB disk.</summary>
public sealed record UsbDrive(string Letter, string? Label, string? FileSystem, long? TotalBytes);

/// <summary>What Windows created for a USB device: child devices, drive letters and COM ports.</summary>
public sealed record UsbDeviceResources(
    IReadOnlyList<PnpChildDevice> ChildDevices,
    IReadOnlyList<UsbDrive> Drives,
    IReadOnlyList<string> ComPorts)
{
    public static readonly UsbDeviceResources Empty = new([], [], []);
    public bool IsEmpty => ChildDevices.Count == 0 && Drives.Count == 0 && ComPorts.Count == 0;
}

/// <summary>A full point-in-time capture of the USB topology.</summary>
public sealed record UsbSnapshot(DateTimeOffset CapturedAt, IReadOnlyList<UsbNode> Controllers)
{
    public IEnumerable<UsbNode> Flatten()
    {
        var stack = new Stack<UsbNode>(Controllers.Reverse());
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }

    /// <summary>Copy with user-defined friendly nicknames attached to matching nodes.</summary>
    public UsbSnapshot ApplyAliases(DeviceAliasStore store) =>
        this with { Controllers = Controllers.Select((c, i) => ApplyNodeAliases(c, NodePaths.ControllerKey(i), store)).ToList() };

    private static UsbNode ApplyNodeAliases(UsbNode node, string pathKey, DeviceAliasStore store)
    {
        var alias = store.GetAlias(node, pathKey);
        var children = node.Children
            .Select((child, index) => ApplyNodeAliases(child, NodePaths.ChildKey(pathKey, child, index), store))
            .ToList();

        return node with
        {
            CustomAlias = alias,
            Children = children,
        };
    }

    /// <summary>Copy with serial numbers (and serial-bearing instance IDs) replaced, safe for sharing.</summary>
    public UsbSnapshot MaskSerials() => this with { Controllers = Controllers.Select(Mask).ToList() };

    private static UsbNode Mask(UsbNode node)
    {
        const string masked = "<masked>";
        var serial = node.Device?.SerialNumber;
        var pnp = node.Pnp;
        if (serial is not null && pnp is not null && pnp.InstanceId.EndsWith(serial, StringComparison.OrdinalIgnoreCase))
            pnp = pnp with { InstanceId = pnp.InstanceId[..^serial.Length] + masked };

        var resources = node.Resources;
        if (serial is { Length: >= 4 } && resources is not null)
        {
            resources = resources with
            {
                ChildDevices = resources.ChildDevices
                    .Select(c => c with { InstanceId = c.InstanceId.Replace(serial, masked, StringComparison.OrdinalIgnoreCase) })
                    .ToList(),
            };
        }

        return node with
        {
            Device = node.Device is { SerialNumber: not null } d ? d with { SerialNumber = masked } : node.Device,
            Pnp = pnp,
            Resources = resources,
            Children = node.Children.Select(Mask).ToList(),
        };
    }
}
