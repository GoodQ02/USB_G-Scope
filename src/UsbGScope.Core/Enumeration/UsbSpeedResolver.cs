using UsbGScope.Core.Model;

namespace UsbGScope.Core.Enumeration;

/// <summary>Combines the legacy speed byte with EX_V2 flags into one speed value.</summary>
public static class UsbSpeedResolver
{
    // USB_NODE_CONNECTION_INFORMATION_EX_V2_FLAGS
    public const uint OperatingAtSuperSpeedOrHigher = 0b0001;
    public const uint SuperSpeedCapableOrHigher = 0b0010;
    public const uint OperatingAtSuperSpeedPlusOrHigher = 0b0100;
    public const uint SuperSpeedPlusCapableOrHigher = 0b1000;

    /// <param name="speedCode">USB_DEVICE_SPEED from CONNECTION_INFORMATION_EX: 0 low, 1 full, 2 high, 3 super.</param>
    /// <param name="v2Flags">Flags from CONNECTION_INFORMATION_EX_V2, or null if that IOCTL failed.</param>
    public static UsbSpeed Resolve(byte speedCode, uint? v2Flags)
    {
        uint flags = v2Flags ?? 0;
        if ((flags & OperatingAtSuperSpeedPlusOrHigher) != 0)
            return UsbSpeed.SuperSpeedPlus;
        if (speedCode == 3 || (flags & OperatingAtSuperSpeedOrHigher) != 0)
            return UsbSpeed.SuperSpeed;
        return speedCode switch
        {
            0 => UsbSpeed.Low,
            1 => UsbSpeed.Full,
            2 => UsbSpeed.High,
            _ => UsbSpeed.Unknown,
        };
    }
}
