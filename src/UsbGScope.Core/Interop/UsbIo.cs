// Hub IOCTL usage and structure layouts follow Microsoft's USBView sample and usbioctl.h.
// USBView: https://github.com/microsoft/Windows-driver-samples/tree/main/usb/usbview (MS-PL)
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using UsbGScope.Core.Model;
using static UsbGScope.Core.Interop.NativeMethods;

namespace UsbGScope.Core.Interop;

/// <summary>
/// USB hub / host-controller IOCTLs. Buffers are parsed by byte offset because usbioctl.h
/// structures are 1-byte packed; offsets are documented next to each read.
/// </summary>
internal static unsafe class UsbIo
{
    // CTL_CODE(FILE_DEVICE_USB = 0x22, function, METHOD_BUFFERED, FILE_ANY_ACCESS) = 0x220000 | (function << 2)
    private const uint IOCTL_USB_GET_ROOT_HUB_NAME = 0x220408;                    // HCD_GET_ROOT_HUB_NAME (258)
    private const uint IOCTL_USB_GET_NODE_INFORMATION = 0x220408;                 // USB_GET_NODE_INFORMATION (258)
    private const uint IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION = 0x220410;  // 260
    private const uint IOCTL_USB_GET_NODE_CONNECTION_NAME = 0x220414;             // 261
    private const uint IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME = 0x220420;   // 264
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX = 0x220448;   // 274
    private const uint IOCTL_USB_GET_HUB_INFORMATION_EX = 0x220454;               // 277
    private const uint IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES = 0x220458;        // 278
    private const uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2 = 0x22045C; // 279

    private const int MaxPipes = 32;
    private const int ConnectionInfoExFixedSize = 35; // up to PipeList[]
    private const int PipeInfoSize = 11;              // USB_ENDPOINT_DESCRIPTOR (7) + ScheduleOffset (4)

    public readonly record struct ConnectionInfo(
        UsbConnectionStatus Status,
        byte[] DeviceDescriptor,
        byte SpeedCode,
        bool IsHub,
        ushort DeviceAddress,
        uint OpenPipes,
        byte CurrentConfiguration);

    public readonly record struct ConnectionInfoV2(uint SupportedProtocols, uint Flags);

    public readonly record struct ConnectorProperties(uint Flags, int CompanionPort, string? CompanionHub);

    public static SafeFileHandle Open(string path) =>
        CreateFile(path, GENERIC_WRITE, FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);

    public static int LastError => Marshal.GetLastPInvokeError();

    private static bool Call(SafeFileHandle handle, uint code, byte[] buffer, int inputLength, out int returned)
    {
        fixed (byte* p = buffer)
        {
            uint bytes = 0;
            bool ok = DeviceIoControl(handle, code, p, (uint)inputLength, p, (uint)buffer.Length, &bytes, 0);
            returned = (int)bytes;
            return ok;
        }
    }

    /// <summary>Root hub symbolic name, e.g. <c>USB#ROOT_HUB30#...#{f18a0e88-...}</c>.</summary>
    public static string? GetRootHubName(SafeFileHandle controller) =>
        GetVariableName(controller, IOCTL_USB_GET_ROOT_HUB_NAME, connectionIndex: null);

    /// <summary>Symbolic name of the downstream hub attached to <paramref name="port"/>.</summary>
    public static string? GetNodeConnectionName(SafeFileHandle hub, uint port) =>
        GetVariableName(hub, IOCTL_USB_GET_NODE_CONNECTION_NAME, port);

    /// <summary>Driver key (e.g. <c>{36fc9e60-...}\0012</c>) of the device on <paramref name="port"/>.</summary>
    public static string? GetDriverKeyName(SafeFileHandle hub, uint port) =>
        GetVariableName(hub, IOCTL_USB_GET_NODE_CONNECTION_DRIVERKEY_NAME, port);

    // USB_ROOT_HUB_NAME:           ULONG ActualLength; WCHAR Name[]                         (name @4)
    // USB_NODE_CONNECTION_NAME / _DRIVERKEY_NAME: ULONG ConnectionIndex; ULONG ActualLength; WCHAR Name[] (name @8)
    private static string? GetVariableName(SafeFileHandle handle, uint code, uint? connectionIndex)
    {
        int header = connectionIndex.HasValue ? 8 : 4;
        int size = 512;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var buffer = new byte[size];
            if (connectionIndex is uint index)
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, index);

            if (!Call(handle, code, buffer, connectionIndex.HasValue ? buffer.Length : 0, out _))
                return null;

            int actual = (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(header - 4));
            if (actual > size)
            {
                size = actual;
                continue;
            }

            int byteCount = Math.Max(0, Math.Min(actual, size) - header) & ~1;
            var chars = MemoryMarshal.Cast<byte, char>(buffer.AsSpan(header, byteCount));
            int nul = chars.IndexOf('\0');
            if (nul >= 0)
                chars = chars[..nul];
            return chars.IsEmpty ? null : new string(chars);
        }

        return null;
    }

    public static UsbHubInfo? GetHubInfo(SafeFileHandle hub, string symbolicName)
    {
        int ports = 0;
        bool? busPowered = null;
        var type = UsbHubType.Unknown;

        // USB_NODE_INFORMATION: NodeType @0 (0 = UsbHub); USB_HUB_DESCRIPTOR @4
        //   (bNumberOfPorts @6, bRemoveAndPowerMask[64] @11..74); HubIsBusPowered @75
        var node = new byte[128];
        bool haveNode = Call(hub, IOCTL_USB_GET_NODE_INFORMATION, node, node.Length, out _)
                        && BinaryPrimitives.ReadUInt32LittleEndian(node) == 0;
        if (haveNode)
        {
            ports = node[6];
            busPowered = node[75] != 0;
        }

        // USB_HUB_INFORMATION_EX: HubType @0 (1 root, 2 USB 2.0, 3 USB 3); HighestPortNumber @4
        var ex = new byte[128];
        if (Call(hub, IOCTL_USB_GET_HUB_INFORMATION_EX, ex, ex.Length, out _))
        {
            uint rawType = BinaryPrimitives.ReadUInt32LittleEndian(ex);
            if (rawType is >= 1 and <= 3)
                type = (UsbHubType)rawType;
            int highest = BinaryPrimitives.ReadUInt16LittleEndian(ex.AsSpan(4));
            if (highest > 0)
                ports = highest;
        }
        else if (!haveNode)
        {
            return null;
        }

        return new UsbHubInfo(type, ports, busPowered, symbolicName);
    }

    // USB_NODE_CONNECTION_INFORMATION_EX (packed):
    //   ConnectionIndex @0, DeviceDescriptor[18] @4, CurrentConfigurationValue @22, Speed @23,
    //   DeviceIsHub @24, DeviceAddress @25 (u16), NumberOfOpenPipes @27 (u32), ConnectionStatus @31 (u32), PipeList @35
    public static ConnectionInfo? GetConnectionInfoEx(SafeFileHandle hub, uint port)
    {
        var buffer = new byte[ConnectionInfoExFixedSize + MaxPipes * PipeInfoSize];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, port);
        if (!Call(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX, buffer, buffer.Length, out _))
            return null;

        return new ConnectionInfo(
            Status: (UsbConnectionStatus)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(31)),
            DeviceDescriptor: buffer[4..22],
            SpeedCode: buffer[23],
            IsHub: buffer[24] != 0,
            DeviceAddress: BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(25)),
            OpenPipes: BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(27)),
            CurrentConfiguration: buffer[22]);
    }

    // USB_NODE_CONNECTION_INFORMATION_EX_V2: ConnectionIndex @0, Length @4, SupportedUsbProtocols @8, Flags @12
    public static ConnectionInfoV2? GetConnectionInfoV2(SafeFileHandle hub, uint port)
    {
        var buffer = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, port);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(4), 16);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 0b100); // request: Usb300 = 1 (as USBView does)
        if (!Call(hub, IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX_V2, buffer, buffer.Length, out _))
            return null;

        return new ConnectionInfoV2(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8)),
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12)));
    }

    // USB_PORT_CONNECTOR_PROPERTIES: ConnectionIndex @0, ActualLength @4, UsbPortProperties @8,
    //   CompanionIndex @12 (u16), CompanionPortNumber @14 (u16), CompanionHubSymbolicLinkName @16
    public static ConnectorProperties? GetPortConnectorProperties(SafeFileHandle hub, uint port)
    {
        var buffer = new byte[1024];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, port);
        if (!Call(hub, IOCTL_USB_GET_PORT_CONNECTOR_PROPERTIES, buffer, buffer.Length, out _))
            return null;

        int actual = Math.Min((int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)), buffer.Length);
        string? companionHub = null;
        if (actual > 16)
        {
            var chars = MemoryMarshal.Cast<byte, char>(buffer.AsSpan(16, (actual - 16) & ~1));
            int nul = chars.IndexOf('\0');
            if (nul >= 0)
                chars = chars[..nul];
            if (!chars.IsEmpty)
                companionHub = new string(chars);
        }

        return new ConnectorProperties(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8)),
            BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(14)),
            companionHub);
    }

    // USB_DESCRIPTOR_REQUEST: ConnectionIndex @0, SetupPacket { bmRequest @4, bRequest @5, wValue @6, wIndex @8, wLength @10 }, Data @12
    public static byte[]? GetDescriptor(SafeFileHandle hub, uint port, byte descriptorType, byte index, ushort languageId, ushort length)
    {
        var buffer = new byte[12 + length];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, port);
        buffer[4] = 0x80; // device-to-host, standard, device
        buffer[5] = 0x06; // GET_DESCRIPTOR
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), (ushort)((descriptorType << 8) | index));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8), languageId);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(10), length);

        if (!Call(hub, IOCTL_USB_GET_DESCRIPTOR_FROM_NODE_CONNECTION, buffer, buffer.Length, out int returned) || returned <= 12)
            return null;

        return buffer[12..returned];
    }
}
