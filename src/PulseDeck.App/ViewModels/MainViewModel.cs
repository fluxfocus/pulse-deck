using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using PulseDeck.Core.Model;

namespace PulseDeck.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public const double RowHeight = 26;

    public WaveformSession Session { get; } = new();

    /// <summary>Hierarchical rows (file/scope headers + signals) for the left browser tree only.</summary>
    public ObservableCollection<WaveRow> Rows { get; } = new();

    /// <summary>Flat, user-orderable list of currently-visible signals — no file/scope separators.
    /// Drives the waveform canvas and values column directly.</summary>
    public ObservableCollection<SignalTreeNode> DisplayList { get; } = new();

    /// <summary>DisplayList narrowed by <see cref="FilterText"/>; what the waveform/values columns actually render.</summary>
    public ObservableCollection<SignalTreeNode> FilteredDisplayList { get; } = new();

    public ObservableCollection<Annotation> Annotations { get; } = new();

    private double _windowStartSeconds;
    public double WindowStartSeconds
    {
        get => _windowStartSeconds;
        set { if (SetField(ref _windowStartSeconds, value)) RaiseScrollDerivedChanged(); }
    }

    private double _windowEndSeconds = 0.001;
    public double WindowEndSeconds
    {
        get => _windowEndSeconds;
        set { if (SetField(ref _windowEndSeconds, value)) RaiseScrollDerivedChanged(); }
    }

    /// <summary>Actual extent of the loaded data (0 if nothing is loaded) — independent of the current
    /// window, unlike <see cref="TotalEndSeconds"/>, so it can safely be used to cap the window itself
    /// without a circular dependency.</summary>
    private double DataEndSeconds => Session.GetMaxEndTimeSeconds();

    /// <summary>Furthest timestamp across all loaded files — the right edge of the scrollable range.
    /// Kept at least as large as the current window so the scrollbar never reports an invalid range
    /// before any file is loaded.</summary>
    public double TotalEndSeconds => Math.Max(DataEndSeconds, WindowEndSeconds);

    public double WindowSpanSeconds => Math.Max(WindowEndSeconds - WindowStartSeconds, 1e-15);

    /// <summary>Furthest the view is allowed to zoom out: past this, legitimate data would occupy less
    /// than a third of the screen, which is never useful. No limit until a file is actually loaded.</summary>
    public double MaxWindowSpanSeconds => DataEndSeconds > 0 ? DataEndSeconds * 3.0 : double.MaxValue;

    /// <summary>Horizontal scrollbar's Maximum (its Value is the window's left edge, so Maximum is the
    /// furthest the left edge can sit while the right edge stays within TotalEndSeconds).</summary>
    public double ScrollMaximum => Math.Max(0, TotalEndSeconds - WindowSpanSeconds);

    /// <summary>Step sizes for the scrollbar's own built-in arrow/track clicks — small nudges and
    /// half-screen jumps respectively, scaled to the current zoom rather than a fixed absolute step.</summary>
    public double ScrollSmallChange => WindowSpanSeconds * 0.1;
    public double ScrollLargeChange => WindowSpanSeconds * 0.5;

    private void RaiseScrollDerivedChanged()
    {
        OnPropertyChanged(nameof(WindowSpanSeconds));
        OnPropertyChanged(nameof(TotalEndSeconds));
        OnPropertyChanged(nameof(MaxWindowSpanSeconds));
        OnPropertyChanged(nameof(ScrollMaximum));
        OnPropertyChanged(nameof(ScrollSmallChange));
        OnPropertyChanged(nameof(ScrollLargeChange));
    }

    /// <summary>Moves the visible window to start at the given time, keeping its span unchanged.
    /// Used by the horizontal scrollbar.</summary>
    public void PanTo(double newWindowStart)
    {
        double span = WindowSpanSeconds;
        WindowStartSeconds = newWindowStart;
        WindowEndSeconds = newWindowStart + span;
    }

    /// <summary>Shifts the window by a fraction of its own current span — e.g. -0.75/-0.25 for the two
    /// "pan left" buttons and +0.25/+0.75 for "pan right", so small nudges don't require the large jumps
    /// a scrollbar-thumb drag can cause.</summary>
    public void PanBySpanFraction(double fraction)
    {
        double span = WindowSpanSeconds;
        double delta = span * fraction;
        double newStart = WindowStartSeconds + delta;

        // Clamp so the window doesn't wander past either end of the loaded data.
        if (newStart < 0) newStart = 0;
        double maxStart = Math.Max(0, TotalEndSeconds - span);
        if (newStart > maxStart) newStart = maxStart;

        PanTo(newStart);
    }

    private double _cursorSeconds;
    public double CursorSeconds { get => _cursorSeconds; set => SetField(ref _cursorSeconds, value); }

    private string _statusMessage = "Ready.";
    public string StatusMessage { get => _statusMessage; set => SetField(ref _statusMessage, value); }

    private string? _currentWorkspacePath;
    public string? CurrentWorkspacePath { get => _currentWorkspacePath; set => SetField(ref _currentWorkspacePath, value); }

    private string _filterText = "";
    /// <summary>Case-insensitive substring filter over signal names. Matching scopes are shown expanded
    /// (without disturbing the user's actual expand/collapse state) so a match is never hidden.</summary>
    public string FilterText
    {
        get => _filterText;
        set { if (SetField(ref _filterText, value)) RebuildRows(); }
    }

    private bool _autoReloadEnabled = true;
    /// <summary>When on, a file is reloaded automatically shortly after it changes on disk — the common
    /// case being a tool overwriting the same path with a fresh capture.</summary>
    public bool AutoReloadEnabled { get => _autoReloadEnabled; set => SetField(ref _autoReloadEnabled, value); }

    private double _rulerFontSize = 11;
    public double RulerFontSize
    {
        get => _rulerFontSize;
        set { if (SetField(ref _rulerFontSize, Math.Clamp(value, 7, 28))) OnPropertyChanged(nameof(RulerHeight)); }
    }

    private bool _rulerTextVertical;
    public bool RulerTextVertical
    {
        get => _rulerTextVertical;
        set { if (SetField(ref _rulerTextVertical, value)) OnPropertyChanged(nameof(RulerHeight)); }
    }

    /// <summary>Header-row height shared by the order list, waveform canvas, and values columns — grows
    /// with the ruler font size, and more so when labels are rotated vertical.</summary>
    public double RulerHeight => RulerTextVertical ? Math.Max(55, RulerFontSize * 6.5) : Math.Max(22, RulerFontSize + 16);

    public void IncreaseRulerFont() => RulerFontSize += 1;
    public void DecreaseRulerFont() => RulerFontSize -= 1;

    private bool _showGrid = true;
    public bool ShowGrid { get => _showGrid; set => SetField(ref _showGrid, value); }

    private SignalNameDisplayMode _globalNameDisplayMode = SignalNameDisplayMode.AliasOnly;
    /// <summary>Default name display mode for any signal without its own override.</summary>
    public SignalNameDisplayMode GlobalNameDisplayMode
    {
        get => _globalNameDisplayMode;
        set
        {
            if (SetField(ref _globalNameDisplayMode, value))
            {
                OnPropertyChanged(nameof(GlobalNameDisplayModeIndex));
                RecomputeEffectiveNameModes();
            }
        }
    }

    /// <summary>0/1/2 convenience view of <see cref="GlobalNameDisplayMode"/> for a plain ComboBox binding.</summary>
    public int GlobalNameDisplayModeIndex
    {
        get => (int)GlobalNameDisplayMode;
        set => GlobalNameDisplayMode = (SignalNameDisplayMode)value;
    }

    private void RecomputeEffectiveNameModes()
    {
        foreach (var node in Session.EnumerateAllSignalNodes())
            node.EffectiveNameDisplayMode = node.NameDisplayModeOverride ?? GlobalNameDisplayMode;
    }

    /// <summary>Sets this signal's own display mode (null = follow the global default). Since the tree
    /// row and the flat display-list row are the same SignalTreeNode instance, this updates both panels
    /// at once with no extra wiring.</summary>
    public void SetNodeNameDisplayMode(SignalTreeNode node, SignalNameDisplayMode? mode)
    {
        node.NameDisplayModeOverride = mode;
        node.EffectiveNameDisplayMode = mode ?? GlobalNameDisplayMode;
    }

    private MarkupTool _activeTool = MarkupTool.Select;
    public MarkupTool ActiveTool { get => _activeTool; set => SetField(ref _activeTool, value); }

    private readonly HashSet<SignalTreeNode> _subscribed = new();
    private readonly Dictionary<LoadedFile, FileSystemWatcher> _watchers = new();
    private readonly Dictionary<LoadedFile, CancellationTokenSource> _debounceTokens = new();

    public AsyncRelayCommand AddFileCommand { get; }
    public AsyncRelayCommand ReloadAllCommand { get; }
    public AsyncRelayCommand SaveWorkspaceCommand { get; }
    public AsyncRelayCommand SaveWorkspaceAsCommand { get; }
    public AsyncRelayCommand OpenWorkspaceCommand { get; }
    public RelayCommand ZoomFitCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }

    public MainViewModel()
    {
        AddFileCommand = new AsyncRelayCommand(AddFileAsync);
        ReloadAllCommand = new AsyncRelayCommand(ReloadAllAsync);
        SaveWorkspaceCommand = new AsyncRelayCommand(SaveWorkspaceAsync);
        SaveWorkspaceAsCommand = new AsyncRelayCommand(SaveWorkspaceAsAsync);
        OpenWorkspaceCommand = new AsyncRelayCommand(OpenWorkspaceAsync);
        ZoomFitCommand = new RelayCommand(ZoomFit);
        ZoomInCommand = new RelayCommand(() => ZoomAroundCursor(0.5));
        ZoomOutCommand = new RelayCommand(() => ZoomAroundCursor(2.0));
    }

    public async Task AddFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add VCD File",
            Filter = "VCD files (*.vcd)|*.vcd|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var path in dialog.FileNames)
            await AddFileAsync(path);

        ZoomFit();
    }

    public async Task AddFileAsync(string path)
    {
        var outcome = await Session.AddFileAsync(path);
        SubscribeTree(Session.RootNodes[^1]);
        RebuildRows();
        StartWatching(outcome.File);
        StatusMessage = outcome.Success
            ? $"Loaded {Path.GetFileName(path)}: {outcome.File.Document?.AllVariables.Count ?? 0} signals."
            : $"Failed to load {Path.GetFileName(path)}: {outcome.File.LastError}";
    }

    public async Task ReloadFileAsync(LoadedFile file)
    {
        var outcome = await Session.ReloadFileAsync(file);
        RebuildRows();
        StatusMessage = outcome.Success
            ? $"Reloaded {file.FileAlias}: +{outcome.Summary.AddedPaths.Count} new, -{outcome.Summary.RemovedPaths.Count} missing, {outcome.Summary.UnchangedPaths.Count} unchanged."
            : $"Reload failed for {file.FileAlias}: {file.LastError}";
    }

    public async Task ReloadAllAsync()
    {
        var outcomes = await Session.ReloadAllAsync();
        RebuildRows();
        int added = outcomes.Sum(o => o.Summary.AddedPaths.Count);
        int removed = outcomes.Sum(o => o.Summary.RemovedPaths.Count);
        StatusMessage = $"Reloaded {outcomes.Count} file(s): +{added} new signals, -{removed} missing.";
    }

    public void RemoveFile(LoadedFile file)
    {
        var node = Session.RootNodes.FirstOrDefault(n => n.File == file);
        if (node != null)
            foreach (var n in node.EnumerateSelfAndAllDescendants())
                if (_subscribed.Remove(n)) n.PropertyChanged -= OnNodePropertyChanged;

        StopWatching(file);
        Session.RemoveFile(file);
        RebuildRows();
    }

    private void StartWatching(LoadedFile file)
    {
        try
        {
            string? dir = Path.GetDirectoryName(file.FilePath);
            string name = Path.GetFileName(file.FilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            var watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime
            };
            watcher.Changed += (_, _) => QueueAutoReload(file);
            watcher.Created += (_, _) => QueueAutoReload(file);
            watcher.Renamed += (_, _) => QueueAutoReload(file);
            watcher.EnableRaisingEvents = true;
            _watchers[file] = watcher;
        }
        catch
        {
            // File watching is a convenience on top of the manual Reload button, not load-bearing —
            // if it can't be set up (e.g. a network share quirk), just skip it silently.
        }
    }

    private void StopWatching(LoadedFile file)
    {
        if (_watchers.Remove(file, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        if (_debounceTokens.Remove(file, out var cts))
            cts.Cancel();
    }

    /// <summary>Debounces the flurry of FileSystemWatcher events a single overwrite can raise, then
    /// reloads on the UI thread once the file has been quiet for a short interval.</summary>
    private void QueueAutoReload(LoadedFile file)
    {
        if (_debounceTokens.TryGetValue(file, out var existing))
            existing.Cancel();

        var cts = new CancellationTokenSource();
        _debounceTokens[file] = cts;
        var token = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }
            if (token.IsCancellationRequested) return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (!AutoReloadEnabled || !Session.Files.Contains(file)) return;
                await ReloadFileAsync(file);
            }));
        }, token);
    }

    public void ToggleExpand(SignalTreeNode node) => node.IsExpanded = !node.IsExpanded;

    public void SetAlias(SignalTreeNode node, string? alias) => node.Alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();

    public void SetFileAlias(LoadedFile file, string alias)
    {
        alias = alias.Trim();
        if (string.IsNullOrWhiteSpace(alias) || alias == file.FileAlias) return;

        file.FileAlias = alias;
        var node = Session.RootNodes.FirstOrDefault(n => n.File == file);
        if (node == null) return;

        node.Alias = alias;
        // Keep each signal's stable identity key in sync with the new file alias, or workspace
        // save/reload would look up settings under the old name and silently drop them.
        foreach (var s in node.EnumerateSignalDescendants())
        {
            if (s.Key is { } key) s.Key = key with { FileAlias = alias };
            s.RefreshNameDisplay(); // FileSuffixText reads File.FileAlias, a different object — won't self-notify.
        }
    }

    private void SubscribeTree(SignalTreeNode root)
    {
        foreach (var n in root.EnumerateSelfAndAllDescendants())
            SubscribeNode(n);
    }

    private void SubscribeNode(SignalTreeNode node)
    {
        if (_subscribed.Add(node)) node.PropertyChanged += OnNodePropertyChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SignalTreeNode.IsExpanded) or nameof(SignalTreeNode.IsVisible))
            RebuildRows();
    }

    public void RebuildRows()
    {
        Rows.Clear();
        string filter = FilterText.Trim();
        bool filtering = filter.Length > 0;

        foreach (var root in Session.RootNodes)
        {
            SubscribeTree(root);
            Walk(root, 0);
        }

        void Walk(SignalTreeNode node, int depth)
        {
            if (!filtering)
            {
                if (node.Kind == SignalNodeKind.Signal && !node.IsVisible) return;
                Rows.Add(new WaveRow(node, depth));
                if (node.Kind != SignalNodeKind.Signal && node.IsExpanded)
                    foreach (var child in node.Children) Walk(child, depth + 1);
                return;
            }

            if (node.Kind == SignalNodeKind.Signal)
            {
                if (!node.IsVisible || !MatchesFilter(node, filter)) return;
                Rows.Add(new WaveRow(node, depth));
                return;
            }

            if (!node.EnumerateSignalDescendants().Any(s => s.IsVisible && MatchesFilter(s, filter))) return;
            Rows.Add(new WaveRow(node, depth));
            foreach (var child in node.Children) Walk(child, depth + 1);
        }

        RecomputeEffectiveNameModes();
        SyncDisplayList();
        RaiseScrollDerivedChanged(); // a reload/add/remove can change TotalEndSeconds without touching the window itself
    }

    private static bool MatchesFilter(SignalTreeNode node, string filter) =>
        node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
        node.OriginalName.Contains(filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Keeps <see cref="DisplayList"/> in sync with which signals are checked visible: drops
    /// ones hidden/removed, appends newly-visible ones at the end, and leaves everything else exactly
    /// where the user put it — this is the only place display order can change other than an explicit
    /// move/remove action.</summary>
    private void SyncDisplayList()
    {
        var visibleSignals = new HashSet<SignalTreeNode>(Session.EnumerateAllSignalNodes().Where(s => s.IsVisible));

        for (int i = DisplayList.Count - 1; i >= 0; i--)
            if (!visibleSignals.Contains(DisplayList[i])) DisplayList.RemoveAt(i);

        var existing = new HashSet<SignalTreeNode>(DisplayList);
        foreach (var s in Session.EnumerateAllSignalNodes())
            if (visibleSignals.Contains(s) && existing.Add(s))
                DisplayList.Add(s);

        RebuildFilteredDisplayList();
    }

    private void RebuildFilteredDisplayList()
    {
        FilteredDisplayList.Clear();
        string filter = FilterText.Trim();
        foreach (var s in DisplayList)
            if (filter.Length == 0 || MatchesFilter(s, filter))
                FilteredDisplayList.Add(s);
    }

    /// <summary>Moves a signal one slot up (-1) or down (+1) in the waveform display order.</summary>
    public void MoveDisplayedSignal(SignalTreeNode node, int direction)
    {
        int idx = DisplayList.IndexOf(node);
        if (idx < 0) return;
        int newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= DisplayList.Count) return;
        DisplayList.Move(idx, newIdx);
        RebuildFilteredDisplayList();
    }

    /// <summary>Hides a signal from the waveform display (unchecks it) without removing its source file.</summary>
    public void RemoveFromDisplay(SignalTreeNode node) => node.IsVisible = false;

    public void ZoomFit()
    {
        double end = Session.GetMaxEndTimeSeconds();
        WindowStartSeconds = 0;
        WindowEndSeconds = end > 0 ? end : 0.001;
    }

    public void ZoomAroundCursor(double factor)
    {
        double span = WindowEndSeconds - WindowStartSeconds;
        double center = CursorSeconds is var c && c >= WindowStartSeconds && c <= WindowEndSeconds
            ? c
            : (WindowStartSeconds + WindowEndSeconds) / 2;
        double newSpan = Math.Max(span * factor, 1e-15);
        if (factor > 1.0) newSpan = Math.Min(newSpan, MaxWindowSpanSeconds); // zooming out — don't pass the 1/3-screen floor
        double actualFactor = newSpan / span;
        WindowStartSeconds = center - (center - WindowStartSeconds) * actualFactor;
        WindowEndSeconds = WindowStartSeconds + newSpan;
    }

    private async Task SaveWorkspaceAsync()
    {
        if (CurrentWorkspacePath == null)
        {
            await SaveWorkspaceAsAsync();
            return;
        }
        WorkspaceSerializer.Save(BuildWorkspaceDocument(), CurrentWorkspacePath);
        StatusMessage = $"Workspace saved to {CurrentWorkspacePath}.";
        await Task.CompletedTask;
    }

    private async Task SaveWorkspaceAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Workspace",
            Filter = "Pulse Deck workspace (*.pulsedeck)|*.pulsedeck|All files (*.*)|*.*",
            DefaultExt = ".pulsedeck"
        };
        if (dialog.ShowDialog() != true) return;

        CurrentWorkspacePath = dialog.FileName;
        WorkspaceSerializer.Save(BuildWorkspaceDocument(), CurrentWorkspacePath);
        StatusMessage = $"Workspace saved to {CurrentWorkspacePath}.";
        await Task.CompletedTask;
    }

    private async Task OpenWorkspaceAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Workspace",
            Filter = "Pulse Deck workspace (*.pulsedeck)|*.pulsedeck|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var n in Session.RootNodes.SelectMany(r => r.EnumerateSelfAndAllDescendants()).ToArray())
            if (_subscribed.Remove(n)) n.PropertyChanged -= OnNodePropertyChanged;
        foreach (var f in Session.Files.ToArray())
            StopWatching(f);

        var doc = WorkspaceSerializer.Load(dialog.FileName);
        await Session.LoadFromWorkspaceAsync(doc);
        CurrentWorkspacePath = dialog.FileName;
        foreach (var f in Session.Files)
            StartWatching(f);

        WindowStartSeconds = doc.View.WindowStartSeconds;
        WindowEndSeconds = doc.View.WindowEndSeconds > doc.View.WindowStartSeconds ? doc.View.WindowEndSeconds : Session.GetMaxEndTimeSeconds();
        CursorSeconds = doc.View.CursorSeconds;

        if (Enum.TryParse<SignalNameDisplayMode>(doc.GlobalNameDisplayMode, out var globalMode))
            _globalNameDisplayMode = globalMode; // set the backing field directly; RebuildRows below applies it
        OnPropertyChanged(nameof(GlobalNameDisplayMode));
        OnPropertyChanged(nameof(GlobalNameDisplayModeIndex));

        RebuildRows();

        // Restore the saved display order (SyncDisplayList above only guarantees membership, in tree order).
        var orderByKey = doc.Signals.Where(s => s.Visible)
            .ToDictionary(s => new SignalKey(s.FileAlias, s.HierarchicalPath), s => s.Order);
        var ordered = DisplayList
            .OrderBy(n => n.Key is { } k && orderByKey.TryGetValue(k, out var o) ? o : int.MaxValue)
            .ToList();
        DisplayList.Clear();
        foreach (var n in ordered) DisplayList.Add(n);
        RebuildFilteredDisplayList();

        Annotations.Clear();
        foreach (var a in doc.Annotations) Annotations.Add(a);

        StatusMessage = $"Workspace loaded from {dialog.FileName}.";
    }

    private WorkspaceDocument BuildWorkspaceDocument()
    {
        var view = new WorkspaceViewState
        {
            WindowStartSeconds = WindowStartSeconds,
            WindowEndSeconds = WindowEndSeconds,
            CursorSeconds = CursorSeconds
        };
        var doc = Session.ToWorkspaceDocument(view);

        var orderByKey = new Dictionary<SignalKey, int>();
        for (int i = 0; i < DisplayList.Count; i++)
            if (DisplayList[i].Key is { } key) orderByKey[key] = i;

        foreach (var entry in doc.Signals)
        {
            var key = new SignalKey(entry.FileAlias, entry.HierarchicalPath);
            if (orderByKey.TryGetValue(key, out var order)) entry.Order = order;
        }

        doc.Annotations = Annotations.ToList();
        doc.GlobalNameDisplayMode = GlobalNameDisplayMode.ToString();
        return doc;
    }
}
