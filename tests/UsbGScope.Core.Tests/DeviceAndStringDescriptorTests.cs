using System.Text;
using UsbGScope.Core.Descriptors;

namespace UsbGScope.Core.Tests;

public class DeviceAndStringDescriptorTests
{
    [Fact]
    public void Parses_real_logitech_receiver_device_descriptor()
    {
        var d = UsbDescriptorParser.ParseDevice(Fixture.Load("logitech-c548.device"))!.Value;

        Assert.Equal(0x0200, d.BcdUsb);
        Assert.Equal(0x00, d.DeviceClass);
        Assert.Equal(64, d.MaxPacketSize0);
        Assert.Equal(0x046D, d.VendorId);
        Assert.Equal(0xC548, d.ProductId);
        Assert.Equal(0x0502, d.BcdDevice);
        Assert.Equal(1, d.ManufacturerIndex);
        Assert.Equal(2, d.ProductIndex);
        Assert.Equal(0, d.SerialNumberIndex);
        Assert.Equal(1, d.NumConfigurations);
    }

    [Fact]
    public void Parses_real_superspeed_webcam_device_descriptor()
    {
        var d = UsbDescriptorParser.ParseDevice(Fixture.Load("dell-webcam-wb7022.device"))!.Value;

        Assert.Equal(0x0320, d.BcdUsb);
        Assert.Equal((0xEF, 0x02, 0x01), (d.DeviceClass, d.DeviceSubClass, d.DeviceProtocol)); // IAD composite
        Assert.Equal(9, d.MaxPacketSize0); // SuperSpeed: 2^9 = 512
        Assert.Equal(0x413C, d.VendorId);
        Assert.Equal(0xC015, d.ProductId);
        Assert.Equal(3, d.SerialNumberIndex);
    }

    [Fact]
    public void Device_descriptor_shorter_than_18_bytes_is_rejected()
    {
        Assert.Null(UsbDescriptorParser.ParseDevice(Fixture.Load("logitech-c548.device")[..17]));
    }

    private static byte[] StringDescriptor(string text, int? declaredLength = null, byte type = 0x03)
    {
        var body = Encoding.Unicode.GetBytes(text);
        return [(byte)(declaredLength ?? body.Length + 2), type, .. body];
    }

    [Fact]
    public void Decodes_utf16_string_descriptor()
    {
        Assert.Equal("Stream Deck Neo", UsbDescriptorParser.ParseString(StringDescriptor("Stream Deck Neo")));
    }

    [Fact]
    public void Decodes_non_ascii_string_descriptor()
    {
        Assert.Equal("Héllo – 日本", UsbDescriptorParser.ParseString(StringDescriptor("Héllo – 日本")));
    }

    [Fact]
    public void String_respects_bLength_when_buffer_is_longer()
    {
        // bLength covers only "USB" (2 + 6 bytes); trailing bytes must be ignored.
        Assert.Equal("USB", UsbDescriptorParser.ParseString(StringDescriptor("USB Receiver", declaredLength: 8)));
    }

    [Fact]
    public void String_with_odd_bLength_drops_the_dangling_byte()
    {
        Assert.Equal("US", UsbDescriptorParser.ParseString(StringDescriptor("USB", declaredLength: 7)));
    }

    [Fact]
    public void String_trims_trailing_nulls_and_whitespace()
    {
        Assert.Equal("Receiver", UsbDescriptorParser.ParseString(StringDescriptor("Receiver  \0\0")));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x02, 0x03 })]                 // empty string
    [InlineData(new byte[] { 0x04, 0x02, 0x41, 0x00 })]     // wrong descriptor type
    [InlineData(new byte[] { 0x04, 0x03, 0x00, 0x00 })]     // only a NUL
    public void Invalid_or_empty_string_descriptors_return_null(byte[] data)
    {
        Assert.Null(UsbDescriptorParser.ParseString(data));
    }

    [Fact]
    public void Reads_first_language_id_from_string_descriptor_zero()
    {
        Assert.Equal((ushort)0x0409, UsbDescriptorParser.ParseFirstLanguageId([0x06, 0x03, 0x09, 0x04, 0x07, 0x04]));
        Assert.Null(UsbDescriptorParser.ParseFirstLanguageId([0x02, 0x03]));
    }

    [Theory]
    [InlineData(0x0110, "1.10")]
    [InlineData(0x0200, "2.00")]
    [InlineData(0x0320, "3.20")]
    [InlineData(0x0502, "5.02")]
    public void Formats_bcd_versions(int bcd, string expected)
    {
        Assert.Equal(expected, UsbDescriptorParser.FormatBcd((ushort)bcd));
    }

    [Theory]
    [InlineData(0x03, "HID")]
    [InlineData(0x0E, "Video")]
    [InlineData(0xFF, "Vendor Specific")]
    [InlineData(0x42, "Class 0x42")]
    public void Names_usb_classes(int code, string expected)
    {
        Assert.Equal(expected, UsbClassNames.Get((byte)code));
    }
}
