using System.Collections.ObjectModel;

namespace PulseDeck.Core.Model;

public sealed record ReloadOutcome(LoadedFile File, bool Success, SignalMergeSummary Summary);

/// <summary>
/// Top-level aggregate: the set of loaded VCD files and the merged signal tree built from them.
/// This is the API the UI layer drives — add/remove/reload files, save/restore a workspace.
/// </summary>
public sealed class WaveformSession
{
    public ObservableCollection<LoadedFile> Files { get; } = new();

    /// <summary>One root node per file, in the same order as <see cref="Files"/>.</summary>
    public ObservableCollection<SignalTreeNode> RootNodes { get; } = new();

    public async Task<ReloadOutcome> AddFileAsync(string path, CancellationToken ct = default)
    {
        string alias = MakeUniqueAlias(Path.GetFileNameWithoutExtension(path));
        var file = new LoadedFile(path, alias);
        bool ok = await file.LoadAsync(ct);
        Files.Add(file);
        var (node, summary) = SignalTreeBuilder.BuildOrMerge(null, file, isInitialLoad: true);
        RootNodes.Add(node);
        return new ReloadOutcome(file, ok, summary);
    }

    public async Task<ReloadOutcome> ReloadFileAsync(LoadedFile file, CancellationToken ct = default)
    {
        bool ok = await file.LoadAsync(ct);
        var existingNode = RootNodes.FirstOrDefault(n => n.File == file);
        var (_, summary) = SignalTreeBuilder.BuildOrMerge(existingNode, file, isInitialLoad: false);
        return new ReloadOutcome(file, ok, summary);
    }

    public async Task<IReadOnlyList<ReloadOutcome>> ReloadAllAsync(CancellationToken ct = default)
    {
        var results = new List<ReloadOutcome>();
        foreach (var file in Files.ToArray())
            results.Add(await ReloadFileAsync(file, ct));
        return results;
    }

    public void RemoveFile(LoadedFile file)
    {
        var node = RootNodes.FirstOrDefault(n => n.File == file);
        if (node != null) RootNodes.Remove(node);
        Files.Remove(file);
    }

    public IEnumerable<SignalTreeNode> EnumerateAllSignalNodes() => RootNodes.SelectMany(n => n.EnumerateSignalDescendants());

    /// <summary>Exports every currently-aliased signal as a standalone, re-usable alias file — keyed by
    /// actual filename rather than the session's FileAlias, so it still matches if a file gets reloaded
    /// under a different label, or the same file is loaded into a different workspace later.</summary>
    public AliasFileDocument ToAliasFileDocument()
    {
        var doc = new AliasFileDocument();
        foreach (var node in EnumerateAllSignalNodes())
        {
            if (string.IsNullOrWhiteSpace(node.Alias) || node.File == null || node.Key is not { } key) continue;
            doc.Entries.Add(new AliasFileEntry
            {
                FileName = Path.GetFileName(node.File.FilePath),
                HierarchicalPath = key.HierarchicalPath,
                Alias = node.Alias!
            });
        }
        return doc;
    }

    /// <summary>Applies a saved alias file to whichever currently-loaded signals match by filename +
    /// hierarchical path. Returns how many aliases were applied.</summary>
    public int ApplyAliasFileDocument(AliasFileDocument doc)
    {
        var byKey = doc.Entries.ToDictionary(e => (e.FileName, e.HierarchicalPath), e => e.Alias);
        int applied = 0;
        foreach (var node in EnumerateAllSignalNodes())
        {
            if (node.File == null || node.Key is not { } key) continue;
            string fileName = Path.GetFileName(node.File.FilePath);
            if (byKey.TryGetValue((fileName, key.HierarchicalPath), out var alias))
            {
                node.Alias = alias;
                applied++;
            }
        }
        return applied;
    }

    public double GetMaxEndTimeSeconds() => Files
        .Where(f => f.Document != null)
        .Select(f => f.Document!.EndTimeSeconds)
        .DefaultIfEmpty(0)
        .Max();

    private string MakeUniqueAlias(string baseName)
    {
        string alias = baseName;
        int i = 2;
        while (Files.Any(f => f.FileAlias == alias)) alias = $"{baseName} ({i++})";
        return alias;
    }

    public WorkspaceDocument ToWorkspaceDocument(WorkspaceViewState view)
    {
        var doc = new WorkspaceDocument { View = view };
        foreach (var file in Files)
            doc.Files.Add(new WorkspaceFileEntry { Path = file.FilePath, Alias = file.FileAlias });

        int order = 0;
        foreach (var signal in EnumerateAllSignalNodes())
        {
            if (signal.Key is not { } key) continue;
            doc.Signals.Add(new WorkspaceSignalEntry
            {
                FileAlias = key.FileAlias,
                HierarchicalPath = key.HierarchicalPath,
                Alias = signal.Alias,
                ColorHex = signal.Color?.ToHex(),
                Visible = signal.IsVisible,
                Group = signal.Group,
                Order = order++,
                NameDisplayMode = signal.NameDisplayModeOverride?.ToString()
            });
        }
        return doc;
    }

    public async Task LoadFromWorkspaceAsync(WorkspaceDocument doc, CancellationToken ct = default)
    {
        Files.Clear();
        RootNodes.Clear();

        foreach (var entry in doc.Files)
        {
            var file = new LoadedFile(entry.Path, entry.Alias);
            await file.LoadAsync(ct);
            Files.Add(file);
            var (node, _) = SignalTreeBuilder.BuildOrMerge(null, file, isInitialLoad: true);
            RootNodes.Add(node);
        }

        var settingsByKey = doc.Signals.ToDictionary(s => new SignalKey(s.FileAlias, s.HierarchicalPath));
        foreach (var signalNode in EnumerateAllSignalNodes())
        {
            if (signalNode.Key is not { } key) continue;
            if (!settingsByKey.TryGetValue(key, out var setting)) continue;
            signalNode.Alias = setting.Alias;
            signalNode.IsVisible = setting.Visible;
            signalNode.Group = setting.Group;
            if (setting.ColorHex != null) signalNode.Color = RgbColor.FromHex(setting.ColorHex);
            if (setting.NameDisplayMode != null && Enum.TryParse<SignalNameDisplayMode>(setting.NameDisplayMode, out var mode))
                signalNode.NameDisplayModeOverride = mode;
        }
    }
}
