namespace PulseDeck.Core.Model;

public enum AnnotationKind { VerticalLine, Text, Arrow }

/// <summary>
/// A user-placed markup on the waveform display: a vertical time marker, a free text note, or an arrow
/// between two (time, row) points — e.g. to call out the delay between transitions on two different
/// signals. Times are snapped to the nearest actual signal transition at creation time (see
/// WaveformCanvas's snapping logic), so markers land on real sample points rather than arbitrary pixels.
/// </summary>
public sealed class Annotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AnnotationKind Kind { get; set; }

    public double TimeSeconds { get; set; }
    /// <summary>Arrow end time; unused for VerticalLine/Text.</summary>
    public double TimeSeconds2 { get; set; }

    /// <summary>Row index (into the flat displayed-signal list) the marker is anchored to vertically.
    /// Unused for VerticalLine, which always spans the full height.</summary>
    public double RowIndex { get; set; }
    /// <summary>Arrow end row index; unused for VerticalLine/Text.</summary>
    public double RowIndex2 { get; set; }

    public string Text { get; set; } = "";
    public string ColorHex { get; set; } = "#FFA726";
}
