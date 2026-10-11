namespace UsbGScope.Core.Model;

/// <summary>Kind of node in the USB topology tree.</summary>
public enum UsbNodeKind
{
    HostController,
    RootHub,
    Hub,
    Port,
    Device,
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

/// <summary>Immutable snapshot of one node in the USB tree.</summary>
public sealed record UsbNode(
    UsbNodeKind Kind,
    string DisplayName,
    IReadOnlyList<UsbNode> Children)
{
    public int? PortNumber { get; init; }
    public ushort? VendorId { get; init; }
    public ushort? ProductId { get; init; }
    public UsbSpeed Speed { get; init; } = UsbSpeed.Unknown;
    public string? DeviceInstanceId { get; init; }
}
