using PulseDeck.Core.Model;

namespace PulseDeck.App.ViewModels;

/// <summary>One flattened display row shared by the tree column, waveform canvas, and values column — keeping
/// all three in lockstep is just a matter of all three being driven by the same row list at the same row height.</summary>
public sealed class WaveRow
{
    public SignalTreeNode Node { get; }
    public int Depth { get; }

    public WaveRow(SignalTreeNode node, int depth)
    {
        Node = node;
        Depth = depth;
    }

    public bool HasChildren => Node.Kind != SignalNodeKind.Signal && Node.Children.Count > 0;
    public bool IsSignalRow => Node.Kind == SignalNodeKind.Signal;
    public bool IsGroupRow => !IsSignalRow;
    public double IndentWidth => 6 + Depth * 14;
}
