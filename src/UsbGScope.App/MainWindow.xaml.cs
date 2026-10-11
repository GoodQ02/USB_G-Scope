using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using UsbGScope.App.ViewModels;
using UsbGScope.Core.Enumeration;
using UsbGScope.Core.Model;
using UsbGScope.Core.Presentation;
using Windows.ApplicationModel.DataTransfer;

namespace UsbGScope.App;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan ChangeBarDuration = TimeSpan.FromSeconds(6);

    private UsbSnapshot? _snapshot;
    private UsbNodeViewModel? _selected;
    private bool _refreshing;
    private bool _refreshPending;
    private UsbChangeWatcher? _watcher;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _changeBarTimer;

    public MainWindow()
    {
        InitializeComponent();
        Title = "USB_G-Scope";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 820));

        _changeBarTimer = DispatcherQueue.CreateTimer();
        _changeBarTimer.Interval = ChangeBarDuration;
        _changeBarTimer.IsRepeating = false;
        _changeBarTimer.Tick += (_, _) => ChangeBar.IsOpen = false;

        Closed += (_, _) => StopWatcher();
        StartWatcher();
        _ = RefreshAsync();
    }

    private void StartWatcher()
    {
        if (_watcher is not null)
            return;
        try
        {
            _watcher = new UsbChangeWatcher();
            _watcher.Changed += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_watcher is not null)
                    _ = RefreshAsync();
            });
        }
        catch (Exception ex)
        {
            LiveUpdates.IsChecked = false;
            ShowChange("Live updates unavailable", ex.Message, InfoBarSeverity.Warning);
        }
    }

    private void StopWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            // A change arrived mid-capture; capture again afterwards so the tree never shows stale state.
            _refreshPending = true;
            return;
        }

        _refreshing = true;
        Busy.IsActive = true;
        RefreshButton.IsEnabled = false;
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var snapshot = await Task.Run(UsbTreeEnumerator.Capture);
            stopwatch.Stop();

            var previous = _snapshot;
            _snapshot = snapshot;
            LoadError.IsOpen = false;
            RebuildTree();
            SummaryText.Text = Summarize(snapshot, stopwatch.ElapsedMilliseconds);

            if (previous is not null && NodePaths.Diff(previous, snapshot) is { IsEmpty: false } diff)
                AnnounceDiff(diff, snapshot.CapturedAt);
        }
        catch (Exception ex)
        {
            LoadError.Message = ex.Message;
            LoadError.IsOpen = true;
        }
        finally
        {
            Busy.IsActive = false;
            RefreshButton.IsEnabled = true;
            _refreshing = false;
        }

        if (_refreshPending)
        {
            _refreshPending = false;
            await RefreshAsync();
        }
    }

    private void AnnounceDiff(NodePaths.SnapshotDiff diff, DateTimeOffset at)
    {
        var lines = new List<string>();
        if (diff.Added.Count > 0)
            lines.Add("Connected: " + string.Join(", ", diff.Added.Select(n => n.DisplayName)));
        if (diff.Removed.Count > 0)
            lines.Add("Disconnected: " + string.Join(", ", diff.Removed.Select(n => n.DisplayName)));

        string title = (diff.Added.Count, diff.Removed.Count) switch
        {
            ( > 0, 0) => "Device connected",
            (0, > 0) => "Device removed",
            _ => "Devices changed",
        };
        ShowChange($"{title} · {at.LocalDateTime:T}", string.Join("\n", lines), InfoBarSeverity.Informational);
    }

    private void ShowChange(string title, string message, InfoBarSeverity severity)
    {
        ChangeBar.Title = title;
        ChangeBar.Message = message;
        ChangeBar.Severity = severity;
        ChangeBar.IsOpen = true;
        _changeBarTimer.Stop();
        _changeBarTimer.Start();
    }

    private void RebuildTree()
    {
        if (_snapshot is null)
            return;

        string? selectedKey = _selected?.Key;
        bool showEmpty = ShowEmptyPorts.IsChecked == true;
        var roots = _snapshot.Controllers
            .Select((controller, index) => new UsbNodeViewModel(controller, showEmpty, NodePaths.ControllerKey(index), _collapsed))
            .ToList();
        DeviceTree.ItemsSource = roots;

        var match = selectedKey is null ? null : Find(roots, selectedKey);
        ShowDetails(match);
        if (match is not null)
        {
            // Containers may not be realized yet; the details pane is already correct either way.
            DispatcherQueue.TryEnqueue(() => DeviceTree.SelectedItem = match);
        }
    }

    private static UsbNodeViewModel? Find(IEnumerable<UsbNodeViewModel> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (node.Key == key)
                return node;
            if (key.StartsWith(node.Key + "/", StringComparison.Ordinal) && Find(node.Children, key) is { } hit)
                return hit;
        }
        return null;
    }

    private void ShowDetails(UsbNodeViewModel? vm)
    {
        _selected = vm;
        if (vm is null)
        {
            EmptyDetails.Visibility = Visibility.Visible;
            DetailsScroller.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyDetails.Visibility = Visibility.Collapsed;
        DetailsScroller.Visibility = Visibility.Visible;

        DetailGlyph.Glyph = vm.Glyph;
        DetailTitle.Text = vm.Title;
        DetailSubtitle.Text = vm.Subtitle;

        DetailWarning.Message = vm.Warning ?? "";
        DetailWarning.IsOpen = vm.Warning is not null;

        DetailSections.ItemsSource = NodePresenter.BuildDetails(vm.Node);

        var raw = new List<string>();
        if (vm.Node.Device?.RawDeviceDescriptor is { } dev)
            raw.Add("Device descriptor\n" + NodePresenter.HexDump(dev));
        if (vm.Node.Device?.RawConfigurationDescriptor is { } cfg)
            raw.Add("Configuration descriptor\n" + NodePresenter.HexDump(cfg));
        RawText.Text = string.Join("\n\n", raw);
        RawExpander.Visibility = raw.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        CopyLabel.Text = "Copy";
        DetailsScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private static string Summarize(UsbSnapshot snapshot, long elapsedMs)
    {
        var nodes = snapshot.Flatten().ToList();
        int hubs = nodes.Count(n => n.Kind is UsbNodeKind.RootHub or UsbNodeKind.Hub);
        int devices = nodes.Count(n => n.Kind == UsbNodeKind.Device);
        int warnings = nodes.Count(n => NodePresenter.Warning(n) is not null);
        string text = $"{snapshot.Controllers.Count} controller{(snapshot.Controllers.Count == 1 ? "" : "s")} · {hubs} hubs · {devices} devices";
        if (warnings > 0)
            text += $" · {warnings} warning{(warnings == 1 ? "" : "s")}";
        return text + $" · {elapsedMs} ms";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void ShowEmptyPorts_Click(object sender, RoutedEventArgs e) => RebuildTree();

    private void LiveUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (LiveUpdates.IsChecked == true)
        {
            StartWatcher();
            _ = RefreshAsync(); // catch up on anything that changed while paused
        }
        else
        {
            StopWatcher();
        }
    }

    private void DeviceTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (args.AddedItems.FirstOrDefault() is UsbNodeViewModel vm)
            ShowDetails(vm);
    }

    private void DeviceTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is UsbNodeViewModel vm)
        {
            vm.IsExpanded = true;
            _collapsed.Remove(vm.Key);
        }
    }

    private void DeviceTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args)
    {
        if (args.Item is UsbNodeViewModel vm)
        {
            vm.IsExpanded = false;
            _collapsed.Add(vm.Key);
        }
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null)
            return;

        var package = new DataPackage();
        package.SetText(NodePresenter.ToText(_selected.Node, NodePresenter.BuildDetails(_selected.Node)));
        Clipboard.SetContent(package);

        CopyLabel.Text = "Copied";
        await Task.Delay(1500);
        CopyLabel.Text = "Copy";
    }
}
