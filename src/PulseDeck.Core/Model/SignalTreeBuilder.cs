using PulseDeck.Core.Vcd;

namespace PulseDeck.Core.Model;

public sealed record SignalMergeSummary(IReadOnlyList<string> AddedPaths, IReadOnlyList<string> RemovedPaths, IReadOnlyList<string> UnchangedPaths);

/// <summary>
/// Builds the signal tree for a <see cref="LoadedFile"/> and, on reload, merges fresh parse results into
/// an existing tree in place: matching nodes by name at each level, keeping existing node identity (so
/// alias/color/visibility survive), appending genuinely new signals, and flagging ones that vanished as
/// <see cref="SignalTreeNode.IsMissing"/> rather than deleting them — captures routinely add or drop
/// a channel between runs and the user should not have to re-set up their view because of it.
/// </summary>
public static class SignalTreeBuilder
{
    public static (SignalTreeNode Node, SignalMergeSummary Summary) BuildOrMerge(SignalTreeNode? existingFileNode, LoadedFile file, bool isInitialLoad)
    {
        var fileNode = existingFileNode ?? new SignalTreeNode(SignalNodeKind.File, file.FileAlias, file: file);
        var added = new List<string>();
        var removed = new List<string>();
        var unchanged = new List<string>();

        if (file.Document != null)
        {
            MergeScope(fileNode, file.Document.RootScope, file, added, removed, unchanged);
        }
        else
        {
            MarkAllMissing(fileNode, removed);
        }

        if (isInitialLoad)
        {
            foreach (var s in fileNode.EnumerateSignalDescendants()) s.IsNew = false;
            added.Clear();
        }

        return (fileNode, new SignalMergeSummary(added, removed, unchanged));
    }

    private static void MergeScope(SignalTreeNode parentNode, VcdScope scope, LoadedFile file,
        List<string> added, List<string> removed, List<string> unchanged)
    {
        var existingByName = parentNode.Children.ToDictionary(c => c.OriginalName);
        var seenNames = new HashSet<string>();

        foreach (var childScope in scope.Children)
        {
            seenNames.Add(childScope.Name);
            if (existingByName.TryGetValue(childScope.Name, out var existingNode) && existingNode.Kind == SignalNodeKind.Scope)
            {
                existingNode.IsMissing = false;
                MergeScope(existingNode, childScope, file, added, removed, unchanged);
            }
            else
            {
                var newScopeNode = new SignalTreeNode(SignalNodeKind.Scope, childScope.Name) { Parent = parentNode };
                MergeScope(newScopeNode, childScope, file, added, removed, unchanged);
                parentNode.Children.Add(newScopeNode);
            }
        }

        foreach (var variable in scope.Variables)
        {
            seenNames.Add(variable.Name);
            string hierPath = variable.HierarchicalPath;

            if (existingByName.TryGetValue(variable.Name, out var existingNode) && existingNode.Kind == SignalNodeKind.Signal)
            {
                existingNode.Variable = variable;
                existingNode.IsMissing = false;
                existingNode.IsNew = false;
                unchanged.Add(hierPath);
            }
            else
            {
                var key = new SignalKey(file.FileAlias, hierPath);
                var newSignalNode = new SignalTreeNode(SignalNodeKind.Signal, variable.Name, file, key)
                {
                    Variable = variable,
                    IsNew = true,
                    Parent = parentNode,
                    Color = RgbColor.FromPalette(parentNode.Children.Count)
                };
                parentNode.Children.Add(newSignalNode);
                added.Add(hierPath);
            }
        }

        foreach (var child in parentNode.Children)
        {
            if (!seenNames.Contains(child.OriginalName) && !child.IsMissing)
            {
                MarkAllMissing(child, removed);
            }
        }
    }

    private static void MarkAllMissing(SignalTreeNode node, List<string> removed)
    {
        node.IsMissing = true;
        node.IsNew = false;
        if (node.Kind == SignalNodeKind.Signal)
        {
            if (node.Key is { } key) removed.Add(key.HierarchicalPath);
        }
        else
        {
            foreach (var c in node.Children) MarkAllMissing(c, removed);
        }
    }
}
