using Microsoft.Win32;
using UsbGScope.Core.Interop;
using UsbGScope.Core.Model;

namespace UsbGScope.Core.Enumeration;

/// <summary>
/// Finds what Windows built on top of each USB device: child device nodes, drive letters, COM ports.
/// Everything here is best effort and read-only; a failure only means less detail, never a failed capture.
/// </summary>
internal sealed class DeviceResourceMapper(PnpDeviceIndex pnp)
{
    private Dictionary<(uint, uint), List<UsbDrive>>? _drivesByDisk;

    public UsbSnapshot Apply(UsbSnapshot snapshot) =>
        snapshot with { Controllers = snapshot.Controllers.Select(Apply).ToList() };

    private UsbNode Apply(UsbNode node)
    {
        var children = node.Children.Select(Apply).ToList();
        UsbDeviceResources? resources = node is { Kind: UsbNodeKind.Device, Pnp: { } info } ? Map(info) : null;
        return node with { Children = children, Resources = resources ?? node.Resources };
    }

    private UsbDeviceResources Map(PnpDeviceInfo device)
    {
        var descendants = pnp.Descendants(device.InstanceId);
        var comPorts = new List<string>();
        var drives = new List<UsbDrive>();

        // The device itself can be the serial port (single-function CDC ACM devices).
        foreach (var info in descendants.Select(d => d.Info).Prepend(device))
        {
            switch (info.ClassName)
            {
                case "Ports" when ReadPortName(info.InstanceId) is { } port:
                    comPorts.Add(port);
                    break;
                case "DiskDrive":
                    drives.AddRange(DrivesFor(info.InstanceId, StorageIo.DiskInterface));
                    break;
                case "CDROM":
                    drives.AddRange(DrivesFor(info.InstanceId, StorageIo.CdRomInterface));
                    break;
            }
        }

        var childDevices = descendants
            .Select(d => new PnpChildDevice(d.Info.InstanceId, d.Info.Description, d.Info.ClassName, d.Info.ProblemCode, d.Depth))
            .ToList();

        return new UsbDeviceResources(
            childDevices,
            drives.DistinctBy(d => d.Letter).OrderBy(d => d.Letter, StringComparer.Ordinal).ToList(),
            comPorts.Distinct(StringComparer.OrdinalIgnoreCase).Order(ComPortComparer.Instance).ToList());
    }

    private IEnumerable<UsbDrive> DrivesFor(string instanceId, Guid interfaceClass)
    {
        foreach (var entry in SafeInterfaces(interfaceClass, instanceId))
        {
            if (StorageIo.Query(entry.DevicePath) is { } number
                && DrivesByDisk().TryGetValue(number.DiskKey, out var list))
            {
                foreach (var drive in list)
                    yield return drive;
            }
        }
    }

    private static List<SetupApi.InterfaceEntry> SafeInterfaces(Guid interfaceClass, string instanceId)
    {
        try { return SetupApi.GetInterfaces(interfaceClass, instanceId); }
        catch { return []; }
    }

    /// <summary>Built lazily: machines without USB storage never touch volumes at all.</summary>
    private Dictionary<(uint, uint), List<UsbDrive>> DrivesByDisk()
    {
        if (_drivesByDisk is not null)
            return _drivesByDisk;

        _drivesByDisk = [];
        DriveInfo[] all;
        try { all = DriveInfo.GetDrives(); }
        catch { return _drivesByDisk; }

        foreach (var drive in all)
        {
            if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory)
                continue;

            string letter = drive.Name.TrimEnd('\\');
            if (StorageIo.Query(@"\\.\" + letter) is not { } number)
                continue;

            string? label = null, fileSystem = null;
            long? size = null;
            try
            {
                if (drive.IsReady)
                {
                    label = string.IsNullOrEmpty(drive.VolumeLabel) ? null : drive.VolumeLabel;
                    fileSystem = drive.DriveFormat;
                    size = drive.TotalSize;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Media removed mid-query or locked volume: keep the letter, skip the details.
            }

            if (!_drivesByDisk.TryGetValue(number.DiskKey, out var list))
                _drivesByDisk[number.DiskKey] = list = [];
            list.Add(new UsbDrive(letter, label, fileSystem, size));
        }

        return _drivesByDisk;
    }

    private static string? ReadPortName(string instanceId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters");
            return key?.GetValue("PortName") as string is { Length: > 0 } name ? name : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Sorts COM2 before COM10.</summary>
internal sealed class ComPortComparer : IComparer<string>
{
    public static readonly ComPortComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        static (string Prefix, int Number) Split(string? s)
        {
            s ??= "";
            int i = s.Length;
            while (i > 0 && char.IsAsciiDigit(s[i - 1]))
                i--;
            return (s[..i], i < s.Length && int.TryParse(s[i..], out int n) ? n : -1);
        }

        var (px, nx) = Split(x);
        var (py, ny) = Split(y);
        int c = string.Compare(px, py, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : nx.CompareTo(ny);
    }
}
