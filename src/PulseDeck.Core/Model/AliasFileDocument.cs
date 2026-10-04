namespace PulseDeck.Core.Model;

/// <summary>
/// One saved alias assignment, keyed by the VCD file's actual filename (not the session's chosen
/// FileAlias) plus the signal's hierarchical path — so a saved alias file stays meaningful across
/// different sessions/workspaces that load "the same" file under a different label.
/// </summary>
public sealed class AliasFileEntry
{
    public string FileName { get; set; } = "";
    public string HierarchicalPath { get; set; } = "";
    public string Alias { get; set; } = "";
}

public sealed class AliasFileDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<AliasFileEntry> Entries { get; set; } = new();
}
