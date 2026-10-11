using System.Text.Json;
using System.Text.Json.Serialization;
using UsbGScope.Core.Model;

namespace UsbGScope.Core.Export;

/// <summary>JSON form of a snapshot, shared by the CLI (--json) and the app's Export command.</summary>
public static class SnapshotJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(UsbSnapshot snapshot, bool maskSerials = false) =>
        JsonSerializer.Serialize(maskSerials ? snapshot.MaskSerials() : snapshot, Options);

    /// <summary>Default export file name, e.g. <c>usb-g-scope-2026-10-11-171400.json</c>.</summary>
    public static string SuggestedFileName(UsbSnapshot snapshot) =>
        $"usb-g-scope-{snapshot.CapturedAt.LocalDateTime:yyyy-MM-dd-HHmmss}";
}
