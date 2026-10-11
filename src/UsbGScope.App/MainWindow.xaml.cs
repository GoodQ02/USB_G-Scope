using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Windows.Storage.Pickers;
using UsbGScope.App.Services;
using UsbGScope.App.ViewModels;
using UsbGScope.Core.Enumeration;
using UsbGScope.Core.Export;
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
    private bool _rebuilding;
    private UsbChangeWatcher? _watcher;
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _changeBarTimer;
    private readonly AppSettings _settings = AppSettings.Load();

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

        ShowEmptyPorts.IsChecked = _settings.ShowEmptyPorts;
        LiveUpdates.IsChecked = _settings.LiveUpdates;
        ApplyTheme(_settings.Theme);

        Closed += (_, _) => StopWatcher();
        if (_settings.LiveUpdates)
            StartWatcher();
        _ = RefreshAsync();
    }

    private void ApplyTheme(AppTheme theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        // Caption buttons (min/max/close) are drawn by the system and need the theme separately.
        AppWindow.TitleBar.PreferredTheme = theme switch
        {
            AppTheme.Light => TitleBarTheme.Light,
            AppTheme.Dark => TitleBarTheme.Dark,
            _ => TitleBarTheme.UseDefaultAppMode,
        };
        ThemeSystem.IsChecked = theme == AppTheme.System;
        ThemeLight.IsChecked = theme == AppTheme.Light;
        ThemeDark.IsChecked = theme == AppTheme.Dark;
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

        int issues = _snapshot.Flatten().Count(n => NodePresenter.Warning(n) is not null);
        if (issues == 0)
            IssuesToggle.IsChecked = false;
        IssuesToggle.Visibility = issues > 0 ? Visibility.Visible : Visibility.Collapsed;
        IssuesLabel.Text = issues == 1 ? "1 issue" : $"{issues} issues";

        string query = SearchBox.Text;
        bool issuesOnly = IssuesToggle.IsChecked == true;
        var visible = NodeFilter.VisibleKeys(_snapshot, query, issuesOnly);

        string? selectedKey = _selected?.Key;
        bool showEmpty = ShowEmptyPorts.IsChecked == true;
        var roots = _snapshot.Controllers
            .Select((controller, index) => (controller, key: NodePaths.ControllerKey(index)))
            .Where(c => visible is null || visible.Contains(c.key))
            .Select(c => new UsbNodeViewModel(c.controller, showEmpty, c.key, _collapsed, visible))
            .ToList();

        // Node mode with explicit IsExpanded: a bound IsExpanded is not re-applied reliably when
        // ItemsSource is swapped, which collapsed the tree on every refresh.
        _rebuilding = true;
        try
        {
            DeviceTree.RootNodes.Clear();
            foreach (var root in roots)
                DeviceTree.RootNodes.Add(ToTreeNode(root));
        }
        finally
        {
            _rebuilding = false;
        }

        NoMatches.Visibility = roots.Count == 0 && visible is not null ? Visibility.Visible : Visibility.Collapsed;
        NoMatches.Text = string.IsNullOrWhiteSpace(query)
            ? "No devices with issues."
            : $"No devices match \"{query.Trim()}\".";

        var match = selectedKey is null ? null : Find(DeviceTree.RootNodes, selectedKey);
        ShowDetails(match?.Content as UsbNodeViewModel);
        if (match is not null)
        {
            // Containers may not be realized yet; the details pane is already correct either way.
            DispatcherQueue.TryEnqueue(() => DeviceTree.SelectedNode = match);
        }
    }

    private static TreeViewNode ToTreeNode(UsbNodeViewModel vm)
    {
        var node = new TreeViewNode { Content = vm };
        foreach (var child in vm.Children)
            node.Children.Add(ToTreeNode(child));
        node.IsExpanded = vm.IsExpanded && node.Children.Count > 0;
        return node;
    }

    private static TreeViewNode? Find(IEnumerable<TreeViewNode> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (node.Content is not UsbNodeViewModel vm)
                continue;
            if (vm.Key == key)
                return node;
            if (key.StartsWith(vm.Key + "/", StringComparison.Ordinal) && Find(node.Children, key) is { } hit)
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
        return text + $" · captured in {elapsedMs} ms · updated {snapshot.CapturedAt.LocalDateTime:T}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void ShowEmptyPorts_Click(object sender, RoutedEventArgs e)
    {
        _settings.ShowEmptyPorts = ShowEmptyPorts.IsChecked == true;
        _settings.Save();
        RebuildTree();
    }

    private void LiveUpdates_Click(object sender, RoutedEventArgs e)
    {
        _settings.LiveUpdates = LiveUpdates.IsChecked == true;
        _settings.Save();
        if (_settings.LiveUpdates)
        {
            StartWatcher();
            _ = RefreshAsync(); // catch up on anything that changed while paused
        }
        else
        {
            StopWatcher();
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => RebuildTree();

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && SearchBox.Text.Length > 0)
        {
            SearchBox.Text = "";
            e.Handled = true;
        }
    }

    private void FocusSearch_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void IssuesToggle_Click(object sender, RoutedEventArgs e) => RebuildTree();

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && Enum.TryParse<AppTheme>(tag, out var theme))
        {
            _settings.Theme = theme;
            _settings.Save();
            ApplyTheme(theme);
        }
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e) => await ExportAsync(maskSerials: false);

    private async void ExportJsonMasked_Click(object sender, RoutedEventArgs e) => await ExportAsync(maskSerials: true);

    private async void SaveJson_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await ExportAsync(maskSerials: false);
    }

    /// <summary>Saves the full (unfiltered) snapshot that is on screen.</summary>
    private async Task ExportAsync(bool maskSerials)
    {
        if (_snapshot is not { } snapshot)
            return;

        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                SuggestedFileName = SnapshotJson.SuggestedFileName(snapshot) + (maskSerials ? "-masked" : ""),
                DefaultFileExtension = ".json",
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeChoices.Add("JSON", [".json"]);

            var result = await picker.PickSaveFileAsync();
            if (result is null)
                return;

            await File.WriteAllTextAsync(result.Path, SnapshotJson.Serialize(snapshot, maskSerials));
            ShowChange("Snapshot exported", result.Path, InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowChange("Export failed", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void DeviceTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (AsViewModel(args.AddedItems.FirstOrDefault()) is { } vm)
            ShowDetails(vm);
    }

    private void DeviceTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args) =>
        RememberExpansion(args.Node, expanded: true);

    private void DeviceTree_Collapsed(TreeView sender, TreeViewCollapsedEventArgs args) =>
        RememberExpansion(args.Node, expanded: false);

    private void RememberExpansion(TreeViewNode? node, bool expanded)
    {
        // Filtering force-expands everything; don't let that (or a rebuild) overwrite the user's choices.
        if (_rebuilding || node?.Content is not UsbNodeViewModel vm || NodeFilter.Tokenize(SearchBox.Text).Length > 0 || IssuesToggle.IsChecked == true)
            return;
        vm.IsExpanded = expanded;
        if (expanded)
            _collapsed.Remove(vm.Key);
        else
            _collapsed.Add(vm.Key);
    }

    private static UsbNodeViewModel? AsViewModel(object? item) => item switch
    {
        TreeViewNode { Content: UsbNodeViewModel vm } => vm,
        UsbNodeViewModel vm => vm,
        _ => null,
    };

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
