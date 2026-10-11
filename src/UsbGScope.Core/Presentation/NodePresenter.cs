using System.Text;
using UsbGScope.Core.Descriptors;
using UsbGScope.Core.Model;

namespace UsbGScope.Core.Presentation;

public sealed record DetailRow(string Label, string Value);

public sealed record DetailSection(string Title, IReadOnlyList<DetailRow> Rows);

/// <summary>Broad device category, used to pick an icon.</summary>
public enum DeviceCategory
{
    Controller,
    Hub,
    EmptyPort,
    Keyboard,
    Mouse,
    Hid,
    Video,
    Audio,
    Storage,
    Bluetooth,
    Printer,
    Other,
}

/// <summary>Turns a <see cref="UsbNode"/> into display-ready text. UI-framework independent.</summary>
public static class NodePresenter
{
    public static string SpeedLabel(UsbSpeed speed) => speed switch
    {
        UsbSpeed.Low => "Low Speed · 1.5 Mbps",
        UsbSpeed.Full => "Full Speed · 12 Mbps",
        UsbSpeed.High => "High Speed · 480 Mbps",
        UsbSpeed.SuperSpeed => "SuperSpeed · 5 Gbps",
        UsbSpeed.SuperSpeedPlus => "SuperSpeed+ · 10 Gbps+",
        _ => "Unknown",
    };

    public static string SpeedBadge(UsbSpeed speed) => speed switch
    {
        UsbSpeed.Low => "1.5M",
        UsbSpeed.Full => "12M",
        UsbSpeed.High => "480M",
        UsbSpeed.SuperSpeed => "5G",
        UsbSpeed.SuperSpeedPlus => "10G+",
        _ => "",
    };

    public static string HubTypeLabel(UsbHubType type) => type switch
    {
        UsbHubType.RootHub => "Root hub",
        UsbHubType.Usb20Hub => "USB 2.0 hub",
        UsbHubType.Usb30Hub => "USB 3 hub",
        _ => "Hub",
    };

    /// <summary>Second line shown under a node's name in the tree.</summary>
    public static string Subtitle(UsbNode node) => node.Kind switch
    {
        UsbNodeKind.HostController => node.Pnp?.Service is { } svc ? $"Host controller · {svc}" : "Host controller",
        UsbNodeKind.RootHub => node.Hub is { } h ? Ports(h.PortCount) : "Root hub",
        UsbNodeKind.EmptyPort => PortCapabilities(node.Port) is { Length: > 0 } caps ? caps : "Empty port",
        _ => string.Join(" · ", new[]
        {
            node.Device is { } d ? $"{d.VendorId:X4}:{d.ProductId:X4}" : null,
            node.Kind == UsbNodeKind.Hub && node.Hub is { } hub ? $"{HubTypeLabel(hub.Type)}, {Ports(hub.PortCount)}" : null,
            node.Pnp?.Description is { } desc && !desc.Equals(node.DisplayName, StringComparison.OrdinalIgnoreCase) ? desc : null,
        }.Where(s => s is not null)),
    };

    /// <summary>Short chips shown on a tree row: drive letters, COM ports, and Type-C.</summary>
    public static IReadOnlyList<string> Badges(UsbNode node)
    {
        var badges = new List<string>();
        if (node.Resources is { } r)
        {
            badges.AddRange(r.Drives.Select(d => d.Letter));
            badges.AddRange(r.ComPorts);
        }
        if (node.Kind is UsbNodeKind.Device or UsbNodeKind.Hub && node.Port?.TypeC == true)
            badges.Add("Type-C");
        return badges;
    }

    /// <summary>"E: F: · COM3", or null when the device has no drive letters or ports.</summary>
    public static string? ResourceSummary(UsbDeviceResources? resources)
    {
        if (resources is null) return null;
        var parts = new List<string>();
        if (resources.Drives.Count > 0) parts.Add(string.Join(" ", resources.Drives.Select(d => d.Letter)));
        if (resources.ComPorts.Count > 0) parts.Add(string.Join(" ", resources.ComPorts));
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    /// <summary>Short warning text for the node, or null if nothing needs attention.</summary>
    public static string? Warning(UsbNode node)
    {
        if (node.Error is not null)
            return node.Error;
        if (node.Pnp?.ProblemCode is uint code)
            return $"Windows reports a problem with this device (code {code}).";
        if (node.Resources?.ChildDevices.FirstOrDefault(c => c.ProblemCode is not null) is { } child)
            return $"Windows reports a problem with \"{child.Description ?? child.InstanceId}\" (code {child.ProblemCode}).";
        if (node.IsSpeedDowngraded)
            return "This device supports SuperSpeed but is running at USB 2.0 speed. Check the cable, port, or hub.";
        return null;
    }

    public static DeviceCategory Categorize(UsbNode node)
    {
        switch (node.Kind)
        {
            case UsbNodeKind.HostController: return DeviceCategory.Controller;
            case UsbNodeKind.RootHub or UsbNodeKind.Hub: return DeviceCategory.Hub;
            case UsbNodeKind.EmptyPort: return DeviceCategory.EmptyPort;
        }

        var d = node.Device;
        if (d is null)
            return DeviceCategory.Other;

        var interfaces = d.Configuration?.Interfaces.Where(i => i.AlternateSetting == 0).ToList() ?? [];
        var classes = d.DeviceClass is 0x00 or 0xEF
            ? interfaces.Select(i => i.InterfaceClass).ToHashSet()
            : [d.DeviceClass];

        if (classes.Contains(0xE0) || string.Equals(node.Pnp?.Service, "BTHUSB", StringComparison.OrdinalIgnoreCase))
            return DeviceCategory.Bluetooth;
        if (classes.Contains(0x0E)) return DeviceCategory.Video;
        if (classes.Contains(0x01)) return DeviceCategory.Audio;
        if (classes.Contains(0x08)) return DeviceCategory.Storage;
        if (classes.Contains(0x07)) return DeviceCategory.Printer;
        if (classes.Contains(0x03))
        {
            // HID boot protocol: 1 = keyboard, 2 = mouse (subclass 1 = boot interface).
            var boot = interfaces.FirstOrDefault(i => i.InterfaceClass == 0x03 && i.InterfaceSubClass == 1);
            return boot?.InterfaceProtocol switch
            {
                1 => DeviceCategory.Keyboard,
                2 => DeviceCategory.Mouse,
                _ => DeviceCategory.Hid,
            };
        }

        return DeviceCategory.Other;
    }

    public static IReadOnlyList<DetailSection> BuildDetails(UsbNode node)
    {
        var sections = new List<DetailSection>();

        var overview = new List<DetailRow> { new("Name", node.DisplayName), new("Type", KindLabel(node.Kind)) };
        if (node.PortNumber is int port) overview.Add(new("Port", port.ToString()));
        if (node.ConnectionStatus is { } status && status != UsbConnectionStatus.DeviceConnected)
            overview.Add(new("Status", status.ToString()));
        if (node.Kind is UsbNodeKind.Device or UsbNodeKind.Hub)
        {
            overview.Add(new("Speed", SpeedLabel(node.Speed)));
            if (node.SuperSpeedCapable is bool ss)
                overview.Add(new("SuperSpeed capable", YesNo(ss)));
        }
        sections.Add(new("Overview", overview));

        if (node.Device is { } d)
        {
            var rows = new List<DetailRow>
            {
                new("Vendor / Product ID", $"{d.VendorId:X4}:{d.ProductId:X4}"),
            };
            AddIf(rows, "Manufacturer", d.Manufacturer);
            AddIf(rows, "Product", d.Product);
            AddIf(rows, "Serial number", d.SerialNumber);
            rows.Add(new("USB version", d.UsbVersion));
            rows.Add(new("Device version", d.DeviceVersion));
            rows.Add(new("Class", $"{d.ClassName} ({d.DeviceClass:X2}/{d.DeviceSubClass:X2}/{d.DeviceProtocol:X2})"));
            if (d.MaxPowerMilliamps is int ma) rows.Add(new("Max power", $"{ma} mA"));
            rows.Add(new("Address", d.DeviceAddress.ToString()));
            rows.Add(new("Open pipes", d.OpenPipes.ToString()));
            sections.Add(new("Device", rows));

            if (d.Configuration is { } c)
                sections.Add(new("Configuration", ConfigurationRows(c)));
        }

        if (node.Hub is { } hub)
        {
            var rows = new List<DetailRow> { new("Hub type", HubTypeLabel(hub.Type)), new("Ports", hub.PortCount.ToString()) };
            if (hub.BusPowered is bool bp) rows.Add(new("Power", bp ? "Bus-powered" : "Self-powered"));
            sections.Add(new("Hub", rows));
        }

        if (node.Port is { } p)
        {
            var rows = new List<DetailRow>();
            if (PortCapabilities(p) is { Length: > 0 } caps) rows.Add(new("Supports", caps));
            if (p.UserConnectable is bool uc) rows.Add(new("User-accessible", YesNo(uc)));
            if (p.CompanionPortNumber is int cp) rows.Add(new("Companion port", cp.ToString()));
            if (rows.Count > 0) sections.Add(new("Port", rows));
        }

        if (node.Pnp is { } pnp)
        {
            var rows = new List<DetailRow> { new("Instance ID", pnp.InstanceId) };
            AddIf(rows, "Description", pnp.Description);
            AddIf(rows, "Device class", pnp.ClassName);
            AddIf(rows, "Driver service", pnp.Service);
            AddIf(rows, "Location", pnp.LocationInformation);
            AddIf(rows, "Driver key", pnp.DriverKey);
            if (pnp.ProblemCode is uint code) rows.Add(new("Problem code", code.ToString()));
            sections.Add(new("Windows", rows));
        }

        if (node.Resources is { } res)
        {
            var rows = new List<DetailRow>();
            foreach (var drive in res.Drives)
                rows.Add(new($"Drive {drive.Letter}", DriveLabel(drive)));
            foreach (var comPort in res.ComPorts)
                rows.Add(new("Serial port", comPort));
            if (rows.Count > 0) sections.Add(new("Resources", rows));

            if (res.ChildDevices.Count > 0)
            {
                sections.Add(new("Windows devices", res.ChildDevices
                    .Select(c => new DetailRow(
                        new string(' ', (c.Depth - 1) * 2) + (c.ClassName ?? "Device"),
                        (c.Description ?? c.InstanceId) + (c.ProblemCode is uint pc ? $" · problem code {pc}" : "")))
                    .ToList()));
            }
        }

        return sections;
    }

    public static string DriveLabel(UsbDrive drive)
    {
        var parts = new List<string>();
        if (drive.Label is not null) parts.Add(drive.Label);
        if (drive.FileSystem is not null) parts.Add(drive.FileSystem);
        if (drive.TotalBytes is long bytes) parts.Add(FormatBytes(bytes));
        return parts.Count > 0 ? string.Join(" · ", parts) : "No media";
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    private static List<DetailRow> ConfigurationRows(ConfigurationDescriptor c)
    {
        var rows = new List<DetailRow>
        {
            new("Power", (c.SelfPowered ? "Self-powered" : "Bus-powered") + (c.RemoteWakeup ? " · remote wakeup" : "")),
        };
        if (c.IsTruncated)
            rows.Add(new("Warning", "Descriptor is truncated or malformed"));
        foreach (var a in c.Associations)
            rows.Add(new($"Function {a.FirstInterface}–{a.FirstInterface + a.InterfaceCount - 1}", a.ClassName));
        foreach (var i in c.Interfaces)
        {
            string value = $"{i.ClassName} ({i.InterfaceSubClass:X2}/{i.InterfaceProtocol:X2})";
            if (i.Endpoints.Count > 0)
                value += "\n" + string.Join("\n", i.Endpoints.Select(EndpointLabel));
            rows.Add(new($"Interface {i.InterfaceNumber}.{i.AlternateSetting}", value));
        }
        return rows;
    }

    public static string EndpointLabel(EndpointDescriptor e) =>
        $"EP{e.Number} {(e.IsIn ? "IN" : "OUT")} · {e.TransferType.ToString().ToLowerInvariant()} · {e.PacketBytes} bytes";

    public static string PortCapabilities(UsbPortInfo? p)
    {
        if (p is null) return "";
        var tags = new List<string>();
        if (p.SupportsUsb3 == true) tags.Add("USB 3");
        if (p.SupportsUsb2 == true) tags.Add("USB 2");
        if (p.TypeC == true) tags.Add("Type-C");
        if (p.UserConnectable == false) tags.Add("internal");
        return string.Join(" · ", tags);
    }

    /// <summary>Classic hex dump: offset, 16 bytes per line.</summary>
    public static string HexDump(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return "";
        var sb = new StringBuilder();
        for (int i = 0; i < bytes.Length; i += 16)
        {
            if (i > 0) sb.Append('\n');
            sb.Append($"{i:X4}  ");
            string hex = Convert.ToHexString(bytes, i, Math.Min(16, bytes.Length - i));
            for (int k = 0; k < hex.Length; k += 2)
            {
                if (k > 0) sb.Append(' ');
                sb.Append(hex, k, 2);
            }
        }
        return sb.ToString();
    }

    /// <summary>Plain-text rendering of the details, for the clipboard.</summary>
    public static string ToText(UsbNode node, IReadOnlyList<DetailSection> sections)
    {
        var sb = new StringBuilder();
        foreach (var section in sections)
        {
            sb.AppendLine($"[{section.Title}]");
            foreach (var row in section.Rows)
                sb.AppendLine($"{row.Label}: {row.Value.Replace("\n", "\n    ")}");
            sb.AppendLine();
        }
        if (node.Device?.RawDeviceDescriptor is { } dev)
            sb.AppendLine("[Device descriptor]").AppendLine(HexDump(dev)).AppendLine();
        if (node.Device?.RawConfigurationDescriptor is { } cfg)
            sb.AppendLine("[Configuration descriptor]").AppendLine(HexDump(cfg));
        return sb.ToString().TrimEnd();
    }

    private static string KindLabel(UsbNodeKind kind) => kind switch
    {
        UsbNodeKind.HostController => "Host controller",
        UsbNodeKind.RootHub => "Root hub",
        UsbNodeKind.Hub => "External hub",
        UsbNodeKind.Device => "Device",
        UsbNodeKind.EmptyPort => "Empty port",
        _ => kind.ToString(),
    };

    private static string Ports(int count) => count == 1 ? "1 port" : $"{count} ports";

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static void AddIf(List<DetailRow> rows, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) rows.Add(new(label, value));
    }
}
