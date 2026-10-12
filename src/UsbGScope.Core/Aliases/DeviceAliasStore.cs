using System.Text.Json;
using UsbGScope.Core.Model;

namespace UsbGScope.Core.Aliases;

/// <summary>
/// Generates stable, non-invasive identity keys for USB nodes without touching hardware or the Windows registry.
/// </summary>
public static class DeviceAliasKeys
{
    /// <summary>
    /// For devices with a valid serial number: portable across ports and hubs (e.g. "sn:046D:C52B:123456").
    /// </summary>
    public static string? GetDeviceSerialKey(UsbNode node)
    {
        if (node.Device is { SerialNumber: { Length: > 0 } serial, VendorId: var vid, ProductId: var pid }
            && !string.IsNullOrWhiteSpace(serial)
            && !IsGenericPlaceholder(serial))
        {
            return $"sn:{vid:X4}:{pid:X4}:{serial.Trim()}";
        }
        return null;
    }

    /// <summary>
    /// Windows PnP device instance path (e.g. "id:USB\VID_046D&PID_C52B\6&2A7E2F8B&0&2").
    /// Deterministically assigned by Windows based on hub + port for devices lacking serial numbers.
    /// </summary>
    public static string? GetInstanceIdKey(UsbNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Pnp?.InstanceId))
            return $"id:{node.Pnp.InstanceId.Trim()}";
        return null;
    }

    /// <summary>
    /// Symbolic name for USB hubs (e.g. "hub:\??\USB#ROOT_HUB30#...").
    /// </summary>
    public static string? GetHubKey(UsbNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Hub?.SymbolicName))
            return $"hub:{node.Hub.SymbolicName.Trim()}";
        return null;
    }

    /// <summary>
    /// Topology path key (e.g. "path:c0/0/1/2").
    /// </summary>
    public static string? GetPathKey(string? nodePath)
    {
        if (!string.IsNullOrWhiteSpace(nodePath))
            return $"path:{nodePath.Trim()}";
        return null;
    }

    /// <summary>
    /// Returns candidate keys in order of specificity:
    /// 1. Hardware serial (portable across any port)
    /// 2. Windows PnP Instance ID (tied to hardware or port by Windows)
    /// 3. Hub symbolic name
    /// 4. Topology path
    /// 5. Display name fallback
    /// </summary>
    public static IEnumerable<string> GetLookupKeys(UsbNode node, string? nodePath = null)
    {
        var sn = GetDeviceSerialKey(node);
        if (sn is not null) yield return sn;

        var id = GetInstanceIdKey(node);
        if (id is not null) yield return id;

        var hub = GetHubKey(node);
        if (hub is not null) yield return hub;

        var path = GetPathKey(nodePath);
        if (path is not null) yield return path;

        if (!string.IsNullOrWhiteSpace(node.DisplayName))
            yield return $"name:{node.DisplayName.Trim()}";
    }

    /// <summary>
    /// Primary key used when writing a new alias for a node.
    /// Prefers hardware serial, falls back to Windows instance ID, hub name, topology path, or display name.
    /// </summary>
    public static string GetPrimarySaveKey(UsbNode node, string? nodePath = null) =>
        GetDeviceSerialKey(node)
        ?? GetInstanceIdKey(node)
        ?? GetHubKey(node)
        ?? GetPathKey(nodePath)
        ?? $"name:{node.DisplayName.Trim()}";

    private static bool IsGenericPlaceholder(string s) =>
        s is "0" or "0000" or "00000000" or "000000000000" or "0001";
}

/// <summary>
/// Local-first, non-invasive storage for user-defined device nicknames.
/// Persisted at %LOCALAPPDATA%\USB_G-Scope\aliases.json using standard JSON.
/// Zero administrator privileges, zero registry mutations, completely read-only towards the OS and hardware.
/// </summary>
public sealed class DeviceAliasStore
{
    private readonly string? _filePath;
    private readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private static readonly Lazy<DeviceAliasStore> LazyInstance = new(() => Load());
    public static DeviceAliasStore Instance => LazyInstance.Value;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "USB_G-Scope", "aliases.json");

    public DeviceAliasStore(string? filePath = null, IDictionary<string, string>? initial = null)
    {
        _filePath = filePath;
        if (initial is not null)
        {
            foreach (var (k, v) in initial)
                _aliases[k] = v;
        }
    }

    public static DeviceAliasStore Load(string? customPath = null)
    {
        string path = customPath ?? DefaultFilePath;
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
                if (dict is not null)
                    return new DeviceAliasStore(path, dict);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fall back gracefully to empty store
        }
        return new DeviceAliasStore(path);
    }

    public string? GetAlias(UsbNode node, string? nodePath = null)
    {
        lock (_lock)
        {
            foreach (var key in DeviceAliasKeys.GetLookupKeys(node, nodePath))
            {
                if (_aliases.TryGetValue(key, out var alias) && !string.IsNullOrWhiteSpace(alias))
                    return alias;
            }
            return null;
        }
    }

    public string? GetAliasByKey(string key)
    {
        lock (_lock)
        {
            return _aliases.TryGetValue(key, out var alias) ? alias : null;
        }
    }

    public void SetAlias(string key, string alias)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        string trimmed = alias.Trim();
        lock (_lock)
        {
            if (string.IsNullOrEmpty(trimmed))
                _aliases.Remove(key);
            else
                _aliases[key] = trimmed;
        }
        Save();
    }

    public void RemoveAlias(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        lock (_lock)
        {
            _aliases.Remove(key);
        }
        Save();
    }

    /// <summary>
    /// Removes any alias associated with any of the candidate lookup keys for this node.
    /// </summary>
    public void RemoveAliasForNode(UsbNode node, string? nodePath = null)
    {
        lock (_lock)
        {
            foreach (var key in DeviceAliasKeys.GetLookupKeys(node, nodePath))
            {
                _aliases.Remove(key);
            }
        }
        Save();
    }

    public void Clear()
    {
        lock (_lock)
        {
            _aliases.Clear();
        }
        Save();
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        lock (_lock)
        {
            return new Dictionary<string, string>(_aliases, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(_filePath))
            return;

        try
        {
            string? dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string json;
            lock (_lock)
            {
                json = JsonSerializer.Serialize(_aliases, JsonOptions);
            }
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: aliases are user convenience
        }
    }
}
