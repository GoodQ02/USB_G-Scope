using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using static UsbGScope.Core.Interop.NativeMethods;

namespace UsbGScope.Core.Interop;

/// <summary>Thin helpers over SetupAPI device-information sets.</summary>
internal static unsafe class SetupApi
{
    public readonly record struct InterfaceEntry(string DevicePath, string? InstanceId);

    /// <summary>Returns the device path and owning instance ID of every present interface of the given class.</summary>
    public static List<InterfaceEntry> GetInterfaces(Guid interfaceClass)
    {
        var result = new List<InterfaceEntry>();
        nint set = SetupDiGetClassDevs(&interfaceClass, null, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == INVALID_HANDLE_VALUE)
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        try
        {
            for (uint i = 0; ; i++)
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)sizeof(SP_DEVICE_INTERFACE_DATA) };
                if (!SetupDiEnumDeviceInterfaces(set, null, &interfaceClass, i, &ifData))
                    break;

                uint required = 0;
                SetupDiGetDeviceInterfaceDetail(set, &ifData, null, 0, &required, null);
                if (required == 0)
                    continue;

                byte* detail = (byte*)NativeMemory.Alloc(required);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize is the fixed-part size: 8 on 64-bit, 6 on 32-bit.
                    *(uint*)detail = IntPtr.Size == 8 ? 8u : 6u;
                    var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)sizeof(SP_DEVINFO_DATA) };
                    if (SetupDiGetDeviceInterfaceDetail(set, &ifData, detail, required, &required, &devInfo))
                    {
                        string path = new((char*)(detail + 4));
                        result.Add(new InterfaceEntry(path, GetInstanceId(set, &devInfo)));
                    }
                }
                finally
                {
                    NativeMemory.Free(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    public static string? GetInstanceId(nint set, SP_DEVINFO_DATA* devInfo)
    {
        const int capacity = 512;
        char* buffer = stackalloc char[capacity];
        uint required = 0;
        return SetupDiGetDeviceInstanceId(set, devInfo, buffer, capacity, &required)
            ? new string(buffer)
            : null;
    }

    /// <summary>Reads a REG_SZ device property (or the first entry of a REG_MULTI_SZ).</summary>
    public static string? GetStringProperty(nint set, SP_DEVINFO_DATA* devInfo, uint property)
    {
        uint regType = 0, required = 0;
        SetupDiGetDeviceRegistryProperty(set, devInfo, property, &regType, null, 0, &required);
        if (required < 2)
            return null;

        var buffer = new byte[required];
        fixed (byte* p = buffer)
        {
            if (!SetupDiGetDeviceRegistryProperty(set, devInfo, property, &regType, p, required, &required))
                return null;
        }

        string text = Encoding.Unicode.GetString(buffer, 0, (int)required);
        int nul = text.IndexOf('\0');
        if (nul >= 0)
            text = text[..nul];
        return text.Length == 0 ? null : text;
    }
}
