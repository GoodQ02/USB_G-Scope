using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace UsbGScope.Core.Interop;

/// <summary>Raw Win32 imports used for USB enumeration. Keep this file declaration-only.</summary>
internal static unsafe partial class NativeMethods
{
    public const uint DIGCF_PRESENT = 0x02;
    public const uint DIGCF_ALLCLASSES = 0x04;
    public const uint DIGCF_DEVICEINTERFACE = 0x10;

    public const uint SPDRP_DEVICEDESC = 0x00;
    public const uint SPDRP_SERVICE = 0x04;
    public const uint SPDRP_DRIVER = 0x09;
    public const uint SPDRP_FRIENDLYNAME = 0x0C;
    public const uint SPDRP_LOCATION_INFORMATION = 0x0D;

    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;

    public const uint CR_SUCCESS = 0;
    public const uint DN_HAS_PROBLEM = 0x00000400;

    public static readonly nint INVALID_HANDLE_VALUE = -1;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nint Reserved;
    }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SetupDiGetClassDevs(Guid* classGuid, string? enumerator, nint hwndParent, uint flags);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInfo(nint deviceInfoSet, uint memberIndex, SP_DEVINFO_DATA* deviceInfoData);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDeviceInterfaces(
        nint deviceInfoSet, SP_DEVINFO_DATA* deviceInfoData, Guid* interfaceClassGuid,
        uint memberIndex, SP_DEVICE_INTERFACE_DATA* deviceInterfaceData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInterfaceDetail(
        nint deviceInfoSet, SP_DEVICE_INTERFACE_DATA* deviceInterfaceData, void* detail,
        uint detailSize, uint* requiredSize, SP_DEVINFO_DATA* deviceInfoData);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceRegistryProperty(
        nint deviceInfoSet, SP_DEVINFO_DATA* deviceInfoData, uint property,
        uint* propertyRegDataType, byte* buffer, uint bufferSize, uint* requiredSize);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstanceIdW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInstanceId(
        nint deviceInfoSet, SP_DEVINFO_DATA* deviceInfoData, char* buffer, uint bufferSize, uint* requiredSize);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_DevNode_Status(uint* status, uint* problemNumber, uint devInst, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(
        string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(
        SafeFileHandle device, uint ioControlCode, void* inBuffer, uint inBufferSize,
        void* outBuffer, uint outBufferSize, uint* bytesReturned, nint overlapped);

    // ---- Device tree walking (cfgmgr32) ----

    public const uint SPDRP_CLASS = 0x07;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const int MAX_DEVICE_ID_LEN = 200;

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Child(uint* child, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Sibling(uint* sibling, uint devInst, uint flags);

    // ---- Hot-plug notifications (cfgmgr32, Windows 8+) ----

    public const uint CM_NOTIFY_FILTER_FLAG_ALL_INTERFACE_CLASSES = 0x00000001;
    public const uint CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE = 0;
    public const uint CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL = 0;
    public const uint CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL = 1;

    /// <summary>CM_NOTIFY_FILTER: 16-byte header + union (largest member: WCHAR[200]) = 416 bytes.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 416)]
    public struct CM_NOTIFY_FILTER
    {
        [FieldOffset(0)] public uint cbSize;
        [FieldOffset(4)] public uint Flags;
        [FieldOffset(8)] public uint FilterType;
        [FieldOffset(12)] public uint Reserved;
        [FieldOffset(16)] public Guid ClassGuid;
    }

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Register_Notification(
        CM_NOTIFY_FILTER* filter, nint context,
        delegate* unmanaged[Stdcall]<nint, nint, uint, nint, uint, uint> callback,
        nint* notifyContext);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Unregister_Notification(nint notifyContext);
}
