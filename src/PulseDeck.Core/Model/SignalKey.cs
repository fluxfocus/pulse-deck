namespace PulseDeck.Core.Model;

/// <summary>
/// Stable identity for a signal that survives file reload and app restart: (file alias, hierarchical
/// path within that file). VCD identifier codes ("!", "#", ...) are not stable across separate dumps
/// of the same source, so they are never used for identity — only the human-readable path is.
/// </summary>
public readonly record struct SignalKey(string FileAlias, string HierarchicalPath)
{
    public override string ToString() => $"{FileAlias}::{HierarchicalPath}";
}
