namespace PulseDeck.Core.Model;

/// <summary>How a signal's name is rendered in the tree and waveform panels.</summary>
public enum SignalNameDisplayMode
{
    /// <summary>The VCD-declared name, with the source file shown in small text.</summary>
    OriginalWithFile,
    /// <summary>The VCD-declared name plus the source file, with the alias (if any) shown too.</summary>
    OriginalWithFileAndAlias,
    /// <summary>Just the alias, falling back to the original name when no alias is set.</summary>
    AliasOnly
}
