namespace PulseDeck.Core.Model;

/// <summary>Serializable snapshot of a session: which files, per-signal display settings, and the view window.</summary>
public sealed class WorkspaceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<WorkspaceFileEntry> Files { get; set; } = new();
    public List<WorkspaceSignalEntry> Signals { get; set; } = new();
    public WorkspaceViewState View { get; set; } = new();
    public List<Annotation> Annotations { get; set; } = new();
    public string GlobalNameDisplayMode { get; set; } = nameof(SignalNameDisplayMode.AliasOnly);
}

public sealed class WorkspaceFileEntry
{
    public string Path { get; set; } = "";
    public string Alias { get; set; } = "";
}

public sealed class WorkspaceSignalEntry
{
    public string FileAlias { get; set; } = "";
    public string HierarchicalPath { get; set; } = "";
    public string? Alias { get; set; }
    public string? ColorHex { get; set; }
    public bool Visible { get; set; } = true;
    public string? Group { get; set; }
    public int Order { get; set; }
    /// <summary>Per-signal name display mode override; null/absent means "use the global default".</summary>
    public string? NameDisplayMode { get; set; }
}

public sealed class WorkspaceViewState
{
    public double WindowStartSeconds { get; set; }
    public double WindowEndSeconds { get; set; }
    public double CursorSeconds { get; set; }
}
