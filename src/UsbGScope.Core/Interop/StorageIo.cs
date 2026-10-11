using Microsoft.Win32.SafeHandles;
using static UsbGScope.Core.Interop.NativeMethods;

namespace UsbGScope.Core.Interop;

/// <summary>Read-only storage queries used to tie drive letters to USB disks.</summary>
internal static unsafe class StorageIo
{
    /// <summary>CTL_CODE(IOCTL_STORAGE_BASE, 0x0420, METHOD_BUFFERED, FILE_ANY_ACCESS).</summary>
    public const uint IOCTL_STORAGE_GET_DEVICE_NUMBER = 0x2D1080;

    public static readonly Guid DiskInterface = new("53F56307-B6BF-11D0-94F2-00A0C91EFB8B");
    public static readonly Guid CdRomInterface = new("53F56308-B6BF-11D0-94F2-00A0C91EFB8B");

    /// <summary>(DeviceType, DeviceNumber) identifies a physical disk; PartitionNumber is ignored for matching.</summary>
    public readonly record struct DeviceNumber(uint DeviceType, uint Number, uint PartitionNumber)
    {
        public (uint, uint) DiskKey => (DeviceType, Number);
    }

    /// <summary>Opens with zero access rights (no admin, no media access) and queries the device number.</summary>
    public static DeviceNumber? Query(string path)
    {
        using SafeFileHandle handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
        if (handle.IsInvalid)
            return null;

        byte* buffer = stackalloc byte[12];
        uint returned = 0;
        return DeviceIoControl(handle, IOCTL_STORAGE_GET_DEVICE_NUMBER, null, 0, buffer, 12, &returned, 0)
            ? ParseDeviceNumber(new ReadOnlySpan<byte>(buffer, (int)returned))
            : null;
    }

    /// <summary>STORAGE_DEVICE_NUMBER: DeviceType u32 @0, DeviceNumber u32 @4, PartitionNumber u32 @8.</summary>
    public static DeviceNumber? ParseDeviceNumber(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 12)
            return null;
        return new DeviceNumber(
            BitConverter.ToUInt32(buffer[..4]),
            BitConverter.ToUInt32(buffer[4..8]),
            BitConverter.ToUInt32(buffer[8..12]));
    }
}
