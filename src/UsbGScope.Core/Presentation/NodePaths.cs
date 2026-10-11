using UsbGScope.Core.Model;

namespace UsbGScope.Core.Presentation;

/// <summary>
/// Stable path keys for tree nodes ("c0/0/22/1": controller index, then port numbers) and
/// snapshot diffing for hot-plug notifications.
/// </summary>
public static class NodePaths
{
    public static string ControllerKey(int index) => $"c{index}";

    public static string ChildKey(string parentKey, UsbNode child, int index) =>
        $"{parentKey}/{child.PortNumber?.ToString() ?? index.ToString()}";

    /// <summary>Every node with its key, depth-first.</summary>
    public static IEnumerable<(string Key, UsbNode Node)> Enumerate(UsbSnapshot snapshot)
    {
        var stack = new Stack<(string, UsbNode)>();
        for (int i = snapshot.Controllers.Count - 1; i >= 0; i--)
            stack.Push((ControllerKey(i), snapshot.Controllers[i]));

        while (stack.Count > 0)
        {
            var (key, node) = stack.Pop();
            yield return (key, node);
            for (int i = node.Children.Count - 1; i >= 0; i--)
                stack.Push((ChildKey(key, node.Children[i], i), node.Children[i]));
        }
    }

    public sealed record SnapshotDiff(IReadOnlyList<UsbNode> Added, IReadOnlyList<UsbNode> Removed, IReadOnlySet<string> AddedKeys)
    {
        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
    }

    /// <summary>
    /// Devices and hubs that appeared or disappeared. Identity is position + VID:PID, so swapping a
    /// device for a different one on the same port reports both a removal and an arrival.
    /// </summary>
    public static SnapshotDiff Diff(UsbSnapshot before, UsbSnapshot after)
    {
        static Dictionary<string, (string Key, UsbNode Node)> Index(UsbSnapshot s) =>
            Enumerate(s)
                .Where(e => e.Node.Kind is UsbNodeKind.Device or UsbNodeKind.Hub)
                .GroupBy(e => Identity(e.Key, e.Node))
                .ToDictionary(g => g.Key, g => g.First());

        var old = Index(before);
        var now = Index(after);
        var added = now.Where(kv => !old.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var removed = old.Where(kv => !now.ContainsKey(kv.Key)).Select(kv => kv.Value.Node).ToList();
        return new SnapshotDiff(
            added.Select(a => a.Node).ToList(),
            removed,
            added.Select(a => a.Key).ToHashSet(StringComparer.Ordinal));
    }

    private static string Identity(string key, UsbNode node) =>
        node.Device is { } d ? $"{key}|{d.VendorId:X4}:{d.ProductId:X4}" : $"{key}|{node.Kind}";
}
