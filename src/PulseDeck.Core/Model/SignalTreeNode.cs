using System.Collections.ObjectModel;
using PulseDeck.Core.Vcd;

namespace PulseDeck.Core.Model;

public enum SignalNodeKind { File, Scope, Signal }

/// <summary>
/// One node in the hierarchical display tree: File -> Scope(s) -> Signal. Signal-kind nodes carry a
/// stable <see cref="Key"/> so user settings (alias/color/visibility) survive a reload even though
/// <see cref="Variable"/> is swapped out from under them.
/// </summary>
public sealed class SignalTreeNode : ObservableObject
{
    public SignalNodeKind Kind { get; }
    public string OriginalName { get; }
    public LoadedFile? File { get; }
    public SignalKey? Key { get; set; }
    public SignalTreeNode? Parent { get; internal set; }

    public ObservableCollection<SignalTreeNode> Children { get; } = new();

    private string? _alias;
    public string? Alias
    {
        get => _alias;
        set
        {
            if (SetField(ref _alias, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(EditableName));
                RaiseNameDisplayChanged();
            }
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? OriginalName : Alias!;

    /// <summary>Read/write view of <see cref="DisplayName"/> for inline-rename text boxes: writing the
    /// original name (or blank) clears the alias instead of storing a redundant explicit one.</summary>
    public string EditableName
    {
        get => DisplayName;
        set => Alias = string.IsNullOrWhiteSpace(value) || value == OriginalName ? null : value;
    }

    private SignalNameDisplayMode? _nameDisplayModeOverride;
    /// <summary>Per-signal override of how the name renders; null means "use the global default".</summary>
    public SignalNameDisplayMode? NameDisplayModeOverride { get => _nameDisplayModeOverride; set => SetField(ref _nameDisplayModeOverride, value); }

    private SignalNameDisplayMode _effectiveNameDisplayMode = SignalNameDisplayMode.AliasOnly;
    /// <summary>Resolved mode (override ?? global default) — kept in sync by the view layer, since only
    /// it knows the current global default; see MainViewModel.RecomputeEffectiveNameModes.</summary>
    public SignalNameDisplayMode EffectiveNameDisplayMode
    {
        get => _effectiveNameDisplayMode;
        set { if (SetField(ref _effectiveNameDisplayMode, value)) RaiseNameDisplayChanged(); }
    }

    /// <summary>The box always edits the alias; it only doubles as the primary label in AliasOnly mode —
    /// otherwise the original name is shown as static text alongside it instead.</summary>
    public bool IsAliasEditableInline => EffectiveNameDisplayMode == SignalNameDisplayMode.AliasOnly;

    public string PrimaryNameText => IsAliasEditableInline ? DisplayName : OriginalName;

    public bool ShowFileSuffix => Kind == SignalNodeKind.Signal && EffectiveNameDisplayMode != SignalNameDisplayMode.AliasOnly && File != null;

    public string FileSuffixText => File?.FileAlias ?? "";

    public bool ShowAliasSuffix => EffectiveNameDisplayMode == SignalNameDisplayMode.OriginalWithFileAndAlias && !string.IsNullOrWhiteSpace(Alias);

    public string AliasSuffixText => Alias ?? "";

    /// <summary>Plain read-only labels for the bulk alias-editor grid.</summary>
    public string FileDisplayName => File?.FileAlias ?? "";
    public string HierarchicalPathDisplay => Key?.HierarchicalPath ?? OriginalName;

    /// <summary>Re-raises every name-display-derived property; called when alias/mode change, and
    /// externally after a file alias rename (a different object's property, so it can't self-notify).</summary>
    public void RefreshNameDisplay() => RaiseNameDisplayChanged();

    private void RaiseNameDisplayChanged()
    {
        OnPropertyChanged(nameof(IsAliasEditableInline));
        OnPropertyChanged(nameof(PrimaryNameText));
        OnPropertyChanged(nameof(ShowFileSuffix));
        OnPropertyChanged(nameof(FileSuffixText));
        OnPropertyChanged(nameof(ShowAliasSuffix));
        OnPropertyChanged(nameof(AliasSuffixText));
        OnPropertyChanged(nameof(FileDisplayName));
        OnPropertyChanged(nameof(HierarchicalPathDisplay));
    }

    private VcdVariable? _variable;
    public VcdVariable? Variable { get => _variable; internal set => SetField(ref _variable, value); }

    private bool _isMissing;
    /// <summary>True when this node existed in a previous load but is absent from the most recent reload.</summary>
    public bool IsMissing { get => _isMissing; set => SetField(ref _isMissing, value); }

    private bool _isNew;
    /// <summary>True when this node was added by the most recent reload (not the file's first load).</summary>
    public bool IsNew { get => _isNew; set => SetField(ref _isNew, value); }

    private bool _isVisible = true;
    public bool IsVisible { get => _isVisible; set => SetField(ref _isVisible, value); }

    private RgbColor? _color;
    public RgbColor? Color { get => _color; set => SetField(ref _color, value); }

    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set => SetField(ref _isExpanded, value); }

    private string? _group;
    public string? Group { get => _group; set => SetField(ref _group, value); }

    public SignalTreeNode(SignalNodeKind kind, string originalName, LoadedFile? file = null, SignalKey? key = null)
    {
        Kind = kind;
        OriginalName = originalName;
        File = file;
        Key = key;
    }

    public IEnumerable<SignalTreeNode> EnumerateSignalDescendants()
    {
        if (Kind == SignalNodeKind.Signal)
        {
            yield return this;
            yield break;
        }
        foreach (var child in Children)
            foreach (var s in child.EnumerateSignalDescendants())
                yield return s;
    }

    public IEnumerable<SignalTreeNode> EnumerateSelfAndAllDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var n in child.EnumerateSelfAndAllDescendants())
                yield return n;
    }
}
