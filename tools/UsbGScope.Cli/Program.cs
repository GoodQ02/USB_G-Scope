using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UsbGScope.Core.Enumeration;
using UsbGScope.Core.Model;

var flags = new HashSet<string>(args, StringComparer.OrdinalIgnoreCase);
if (flags.Contains("-h") || flags.Contains("--help"))
{
    Console.WriteLine("""
        usbgscope - print the USB topology

          --all            include empty ports
          --verbose        show serial numbers, instance IDs, and driver details
          --json           emit the full snapshot as JSON
          --mask-serials   replace serial numbers (safe for sharing)
        """);
    return 0;
}

var snapshot = UsbTreeEnumerator.Capture();
if (flags.Contains("--mask-serials"))
    snapshot = snapshot.MaskSerials();

if (flags.Contains("--json"))
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    Console.OutputEncoding = Encoding.UTF8;
    Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
    return 0;
}

Console.OutputEncoding = Encoding.UTF8;
bool showEmpty = flags.Contains("--all");
bool verbose = flags.Contains("--verbose");

foreach (var controller in snapshot.Controllers)
    Print(controller, "", isLast: true, isTop: true);

var all = snapshot.Flatten().ToList();
Console.WriteLine();
Console.WriteLine(
    $"{snapshot.Controllers.Count} controllers · " +
    $"{all.Count(n => n.Kind is UsbNodeKind.RootHub or UsbNodeKind.Hub)} hubs · " +
    $"{all.Count(n => n.Kind == UsbNodeKind.Device)} devices · " +
    $"{all.Count(n => n.Kind == UsbNodeKind.EmptyPort)} empty ports · " +
    $"{all.Count(n => n.Error is not null)} errors");
return 0;

void Print(UsbNode node, string indent, bool isLast, bool isTop)
{
    string branch = isTop ? "" : isLast ? "└─ " : "├─ ";
    Console.WriteLine(indent + branch + Describe(node));

    string childIndent = isTop ? indent : indent + (isLast ? "   " : "│  ");
    if (verbose)
    {
        foreach (var line in Details(node))
            Console.WriteLine(childIndent + (node.Children.Count > 0 ? "│    " : "     ") + line);
    }

    var children = node.Children.Where(c => showEmpty || c.Kind != UsbNodeKind.EmptyPort || c.Error is not null).ToList();
    for (int i = 0; i < children.Count; i++)
        Print(children[i], childIndent, i == children.Count - 1, isTop: false);
}

string Describe(UsbNode n)
{
    var sb = new StringBuilder();
    if (n.PortNumber is int port)
        sb.Append($"[{port}] ");

    switch (n.Kind)
    {
        case UsbNodeKind.HostController:
            sb.Append(n.DisplayName);
            break;
        case UsbNodeKind.RootHub:
            sb.Append(n.DisplayName);
            if (n.Hub is { } rh)
                sb.Append($" · {rh.PortCount} ports");
            break;
        case UsbNodeKind.EmptyPort:
            sb.Append("(empty)");
            sb.Append(PortTags(n.Port));
            break;
        default:
            sb.Append(n.DisplayName);
            if (n.Device is { } d)
                sb.Append($"  {d.VendorId:X4}:{d.ProductId:X4}");
            sb.Append($"  {SpeedLabel(n.Speed)}");
            if (n.IsSpeedDowngraded)
                sb.Append(" ⚠ SuperSpeed-capable");
            if (n.Device?.MaxPowerMilliamps is int ma)
                sb.Append($"  {ma} mA");
            if (n.Kind == UsbNodeKind.Hub && n.Hub is { } h)
                sb.Append($"  hub · {h.PortCount} ports");
            if (n.Pnp?.Description is { } desc && !desc.Equals(n.DisplayName, StringComparison.OrdinalIgnoreCase))
                sb.Append($"  ({desc})");
            if (n.Pnp?.ProblemCode is uint code)
                sb.Append($"  ⚠ problem code {code}");
            break;
    }

    if (n.Error is not null)
        sb.Append($"  ⚠ {n.Error}");
    return sb.ToString();
}

IEnumerable<string> Details(UsbNode n)
{
    if (n.Device is { } d)
    {
        yield return $"USB {d.UsbVersion} · class {d.DeviceClass:X2}/{d.DeviceSubClass:X2}/{d.DeviceProtocol:X2} · rev {d.DeviceVersion} · addr {d.DeviceAddress}";
        if (d.Manufacturer is not null) yield return $"Manufacturer: {d.Manufacturer}";
        if (d.SerialNumber is not null) yield return $"Serial: {d.SerialNumber}";
    }
    if (n.Pnp is { } p)
    {
        yield return $"Instance: {p.InstanceId}";
        if (p.Service is not null) yield return $"Service: {p.Service}";
    }
}

static string PortTags(UsbPortInfo? p)
{
    if (p is null) return "";
    var tags = new List<string>();
    if (p.SupportsUsb3 == true) tags.Add("USB3");
    else if (p.SupportsUsb2 == true) tags.Add("USB2");
    if (p.TypeC == true) tags.Add("Type-C");
    if (p.UserConnectable == false) tags.Add("internal");
    return tags.Count > 0 ? "  " + string.Join(" · ", tags) : "";
}

static string SpeedLabel(UsbSpeed s) => s switch
{
    UsbSpeed.Low => "Low (1.5M)",
    UsbSpeed.Full => "Full (12M)",
    UsbSpeed.High => "High (480M)",
    UsbSpeed.SuperSpeed => "SuperSpeed (5G)",
    UsbSpeed.SuperSpeedPlus => "SuperSpeed+ (10G+)",
    _ => "speed ?",
};
