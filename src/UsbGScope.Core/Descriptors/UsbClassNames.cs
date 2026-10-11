namespace UsbGScope.Core.Descriptors;

/// <summary>USB-IF base class codes (https://www.usb.org/defined-class-codes).</summary>
public static class UsbClassNames
{
    public static string Get(byte classCode) => classCode switch
    {
        0x00 => "Defined per interface",
        0x01 => "Audio",
        0x02 => "Communications (CDC)",
        0x03 => "HID",
        0x05 => "Physical",
        0x06 => "Image",
        0x07 => "Printer",
        0x08 => "Mass Storage",
        0x09 => "Hub",
        0x0A => "CDC Data",
        0x0B => "Smart Card",
        0x0D => "Content Security",
        0x0E => "Video",
        0x0F => "Personal Healthcare",
        0x10 => "Audio/Video",
        0x11 => "Billboard",
        0x12 => "USB Type-C Bridge",
        0x13 => "Bulk Display",
        0x14 => "MCTP",
        0x3C => "I3C",
        0xDC => "Diagnostic",
        0xE0 => "Wireless Controller",
        0xEF => "Miscellaneous",
        0xFE => "Application Specific",
        0xFF => "Vendor Specific",
        _ => $"Class 0x{classCode:X2}",
    };
}
