namespace UsbGScope.Core.Tests;

/// <summary>Loads hex-encoded descriptor captures from the Fixtures folder.</summary>
internal static class Fixture
{
    public static byte[] Load(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".hex");
        return Hex(File.ReadAllText(path));
    }

    public static byte[] Hex(string text) =>
        text.Split(new[] { ' ', '\r', '\n', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(h => Convert.ToByte(h, 16))
            .ToArray();
}
