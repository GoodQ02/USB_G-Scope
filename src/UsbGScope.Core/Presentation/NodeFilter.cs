using UsbGScope.Core.Model;

namespace UsbGScope.Core.Presentation;

/// <summary>
/// Search and "issues only" filtering. A node is visible when it matches, or when one of its
/// descendants matches (so every hit keeps its path to the controller).
/// </summary>
public static class NodeFilter
{
    public static string[] Tokenize(string? query) =>
        string.IsNullOrWhiteSpace(query) ? [] : query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Keys of visible nodes, or null when no filter is active (show everything).</summary>
    public static IReadOnlySet<string>? VisibleKeys(UsbSnapshot snapshot, string? query, bool issuesOnly)
    {
        var tokens = Tokenize(query);
        if (tokens.Length == 0 && !issuesOnly)
            return null;

        var visible = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Controllers.Count; i++)
            Visit(snapshot.Controllers[i], NodePaths.ControllerKey(i), tokens, issuesOnly, visible);
        return visible;
    }

    private static bool Visit(UsbNode node, string key, string[] tokens, bool issuesOnly, HashSet<string> visible)
    {
        bool any = Matches(node, tokens, issuesOnly);
        for (int i = 0; i < node.Children.Count; i++)
            any |= Visit(node.Children[i], NodePaths.ChildKey(key, node.Children[i], i), tokens, issuesOnly, visible);
        if (any)
            visible.Add(key);
        return any;
    }

    /// <summary>True when the node itself satisfies the filter (every token found, and has an issue if required).</summary>
    public static bool Matches(UsbNode node, string[] tokens, bool issuesOnly)
    {
        if (issuesOnly && NodePresenter.Warning(node) is null)
            return false;
        if (tokens.Length == 0)
            return true;

        var haystack = SearchText(node);
        return tokens.All(t => haystack.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Everything a user might type to find a node: names, IDs, classes, drive letters, ports.</summary>
    public static string SearchText(UsbNode node)
    {
        var parts = new List<string?>
        {
            node.CustomAlias,
            node.DisplayName,
            NodePresenter.Subtitle(node),
            node.PortNumber is int p ? $"Port {p}" : null,
            node.Pnp?.Description,
            node.Pnp?.InstanceId,
            node.Pnp?.Service,
            node.Pnp?.ClassName,
            NodePresenter.Categorize(node).ToString(),
        };

        if (node.Kind is UsbNodeKind.Device or UsbNodeKind.Hub)
            parts.Add(NodePresenter.SpeedLabel(node.Speed));

        if (node.Device is { } d)
        {
            parts.Add($"{d.VendorId:X4}:{d.ProductId:X4}");
            parts.Add($"VID_{d.VendorId:X4}&PID_{d.ProductId:X4}");
            parts.Add(d.Manufacturer);
            parts.Add(d.Product);
            parts.Add(d.SerialNumber);
            parts.Add(d.ClassName);
        }

        if (node.Resources is { } r)
        {
            parts.AddRange(r.Drives.Select(x => x.Letter));
            parts.AddRange(r.Drives.Select(x => x.Label));
            parts.AddRange(r.ComPorts);
            parts.AddRange(r.ChildDevices.Select(c => c.Description));
            parts.AddRange(r.ChildDevices.Select(c => c.ClassName));
        }

        return string.Join('\n', parts.Where(s => !string.IsNullOrEmpty(s)));
    }
}
