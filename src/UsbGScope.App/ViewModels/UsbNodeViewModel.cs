using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using UsbGScope.Core.Model;
using UsbGScope.Core.Presentation;
using Windows.UI;

namespace UsbGScope.App.ViewModels;

/// <summary>Tree row for one USB node. Immutable: the tree is rebuilt on every refresh.</summary>
public sealed class UsbNodeViewModel
{
    private static readonly Dictionary<UsbSpeed, SolidColorBrush> SpeedBrushes = new()
    {
        [UsbSpeed.Low] = new(Color.FromArgb(255, 0x7A, 0x7A, 0x7A)),
        [UsbSpeed.Full] = new(Color.FromArgb(255, 0x7A, 0x7A, 0x7A)),
        [UsbSpeed.High] = new(Color.FromArgb(255, 0x00, 0x67, 0xC0)),
        [UsbSpeed.SuperSpeed] = new(Color.FromArgb(255, 0x0E, 0x7A, 0x0D)),
        [UsbSpeed.SuperSpeedPlus] = new(Color.FromArgb(255, 0x7A, 0x4F, 0xB8)),
        [UsbSpeed.Unknown] = new(Colors.Transparent),
    };

    public UsbNodeViewModel(UsbNode node, bool showEmptyPorts, string key)
    {
        Node = node;
        Key = key;
        Children = node.Children
            .Select((child, index) => (child, index))
            .Where(c => showEmptyPorts || c.child.Kind != UsbNodeKind.EmptyPort || c.child.Error is not null)
            .Select(c => new UsbNodeViewModel(c.child, showEmptyPorts, $"{key}/{c.child.PortNumber?.ToString() ?? c.index.ToString()}"))
            .ToList();
        IsExpanded = node.Kind is UsbNodeKind.HostController or UsbNodeKind.RootHub or UsbNodeKind.Hub;

        string detail = NodePresenter.Subtitle(node);
        Subtitle = node.PortNumber is int port && node.Kind != UsbNodeKind.EmptyPort
            ? (detail.Length > 0 ? $"Port {port} · {detail}" : $"Port {port}")
            : detail;
        Warning = NodePresenter.Warning(node);
    }

    public UsbNode Node { get; }

    /// <summary>Stable path (controller index + port numbers) used to restore selection after refresh.</summary>
    public string Key { get; }

    public IList<UsbNodeViewModel> Children { get; }

    public bool IsExpanded { get; set; }

    public string Title => Node.DisplayName;

    public string Subtitle { get; }

    public string? Warning { get; }

    public string Glyph => GlyphFor(NodePresenter.Categorize(Node));

    public string SpeedBadge => Node.Kind is UsbNodeKind.Device or UsbNodeKind.Hub ? NodePresenter.SpeedBadge(Node.Speed) : "";

    public Brush SpeedBrush => SpeedBrushes[Node.Speed];

    public Visibility SpeedVisibility => SpeedBadge.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility WarningVisibility => Warning is null ? Visibility.Collapsed : Visibility.Visible;

    public double ContentOpacity => Node.Kind == UsbNodeKind.EmptyPort ? 0.55 : 1.0;

    public string AccessibleName =>
        string.Join(", ", new[] { Title, Subtitle, SpeedBadge.Length > 0 ? NodePresenter.SpeedLabel(Node.Speed) : null, Warning }
            .Where(s => !string.IsNullOrEmpty(s)));

    // Segoe Fluent Icons code points (https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font).
    public static string GlyphFor(DeviceCategory category) => category switch
    {
        DeviceCategory.Controller => "\uE950", // Component
        DeviceCategory.Hub => "\uE88E",        // USB
        DeviceCategory.EmptyPort => "\uE88E",  // USB (dimmed)
        DeviceCategory.Keyboard => "\uE765",   // KeyboardClassic
        DeviceCategory.Mouse => "\uE962",      // Mouse
        DeviceCategory.Hid => "\uE7FC",        // Game
        DeviceCategory.Video => "\uE8B8",      // Webcam
        DeviceCategory.Audio => "\uE767",      // Volume
        DeviceCategory.Storage => "\uEDA2",    // HardDrive
        DeviceCategory.Bluetooth => "\uE702",  // Bluetooth
        DeviceCategory.Printer => "\uE749",    // Print
        _ => "\uE772",                         // Devices
    };
}
