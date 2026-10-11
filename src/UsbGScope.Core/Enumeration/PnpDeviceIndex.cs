using UsbGScope.Core.Interop;
using UsbGScope.Core.Model;
using static UsbGScope.Core.Interop.NativeMethods;

namespace UsbGScope.Core.Enumeration;

/// <summary>Index of present PnP devices, keyed by driver key and instance ID.</summary>
public sealed class PnpDeviceIndex
{
    private readonly Dictionary<string, PnpDeviceInfo> _byDriverKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PnpDeviceInfo> _byInstanceId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, uint> _devInstById = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, PnpDeviceInfo> _byDevInst = [];

    public int Count => _byInstanceId.Count;

    public static unsafe PnpDeviceIndex Build()
    {
        var index = new PnpDeviceIndex();
        nint set = SetupDiGetClassDevs(null, null, 0, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == INVALID_HANDLE_VALUE)
            return index;

        try
        {
            for (uint i = 0; ; i++)
            {
                var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
                if (!SetupDiEnumDeviceInfo(set, i, &devInfo))
                    break;

                string? instanceId = SetupApi.GetInstanceId(set, &devInfo);
                if (instanceId is null)
                    continue;

                uint status = 0, problem = 0;
                uint? problemCode = CM_Get_DevNode_Status(&status, &problem, devInfo.DevInst, 0) == CR_SUCCESS
                                    && (status & DN_HAS_PROBLEM) != 0
                    ? problem
                    : null;

                var info = new PnpDeviceInfo(
                    instanceId,
                    SetupApi.GetStringProperty(set, &devInfo, SPDRP_FRIENDLYNAME)
                        ?? SetupApi.GetStringProperty(set, &devInfo, SPDRP_DEVICEDESC),
                    SetupApi.GetStringProperty(set, &devInfo, SPDRP_DRIVER),
                    SetupApi.GetStringProperty(set, &devInfo, SPDRP_SERVICE),
                    SetupApi.GetStringProperty(set, &devInfo, SPDRP_LOCATION_INFORMATION),
                    problemCode)
                {
                    ClassName = SetupApi.GetStringProperty(set, &devInfo, SPDRP_CLASS),
                };

                index._byInstanceId[instanceId] = info;
                index._devInstById[instanceId] = devInfo.DevInst;
                index._byDevInst[devInfo.DevInst] = info;
                if (info.DriverKey is not null)
                    index._byDriverKey[info.DriverKey] = info;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return index;
    }

    public PnpDeviceInfo? FindByDriverKey(string driverKey) =>
        _byDriverKey.GetValueOrDefault(driverKey);

    public PnpDeviceInfo? FindByInstanceId(string instanceId) =>
        _byInstanceId.GetValueOrDefault(instanceId);

    /// <summary>
    /// Depth-first walk of the PnP device tree below <paramref name="instanceId"/> (excluding itself).
    /// Bounded so a pathological tree can never stall a capture.
    /// </summary>
    internal unsafe List<(PnpDeviceInfo Info, int Depth)> Descendants(string instanceId, int maxDepth = 4, int maxNodes = 64)
    {
        var result = new List<(PnpDeviceInfo, int)>();
        if (!_devInstById.TryGetValue(instanceId, out uint root))
            return result;

        void Walk(uint parent, int depth)
        {
            if (depth > maxDepth)
                return;
            uint child;
            if (CM_Get_Child(&child, parent, 0) != CR_SUCCESS)
                return;
            do
            {
                if (result.Count >= maxNodes)
                    return;
                if (_byDevInst.TryGetValue(child, out var info))
                {
                    result.Add((info, depth));
                    Walk(child, depth + 1);
                }
            }
            while (CM_Get_Sibling(&child, child, 0) == CR_SUCCESS);
        }

        Walk(root, 1);
        return result;
    }

    /// <summary>
    /// Maps a hub symbolic link (e.g. <c>USB#ROOT_HUB30#4&amp;1a2b&amp;0&amp;0#{f18a0e88-...}</c>)
    /// to its device instance ID (<c>USB\ROOT_HUB30\4&amp;1a2b&amp;0&amp;0</c>) and looks it up.
    /// </summary>
    public PnpDeviceInfo? FindBySymbolicName(string symbolicName) =>
        SymbolicNameToInstanceId(symbolicName) is { } id ? FindByInstanceId(id) : null;

    /// <summary>Converts a device-interface symbolic link to its device instance ID, or null if malformed.</summary>
    public static string? SymbolicNameToInstanceId(string symbolicName)
    {
        string name = symbolicName;
        if (name.StartsWith(@"\\?\", StringComparison.Ordinal) || name.StartsWith(@"\\.\", StringComparison.Ordinal))
            name = name[4..];

        var parts = name.Split('#');
        return parts.Length >= 4 ? string.Join('\\', parts[..^1]) : null;
    }
}
