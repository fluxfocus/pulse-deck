using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;
using PulseDeck.App.ViewModels;
using PulseDeck.Core.Model;

namespace PulseDeck.App.Controls;

/// <summary>Shows the value each visible signal holds at the cursor time. Row layout mirrors
/// <see cref="WaveformCanvas"/> exactly (same RowHeight/RulerHeight, same flat row list) so the two
/// stay visually aligned.</summary>
public sealed class ValuesPanel : FrameworkElement
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(System.Collections.ObjectModel.ObservableCollection<SignalTreeNode>), typeof(ValuesPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRowsChanged));

    public static readonly DependencyProperty CursorSecondsProperty = DependencyProperty.Register(
        nameof(CursorSeconds), typeof(double), typeof(ValuesPanel),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RulerHeightProperty = DependencyProperty.Register(
        nameof(RulerHeight), typeof(double), typeof(ValuesPanel),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutAffectingChanged));

    public System.Collections.ObjectModel.ObservableCollection<SignalTreeNode>? Rows
    {
        get => (System.Collections.ObjectModel.ObservableCollection<SignalTreeNode>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public double CursorSeconds { get => (double)GetValue(CursorSecondsProperty); set => SetValue(CursorSecondsProperty, value); }
    public double RulerHeight { get => (double)GetValue(RulerHeightProperty); set => SetValue(RulerHeightProperty, value); }

    private const double RowHeight = MainViewModel.RowHeight;

    private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xDA, 0xDE));
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4));
    private static readonly Pen GridPen = new(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1);

    public ValuesPanel()
    {
        ClipToBounds = true;
    }

    private static void OnLayoutAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ValuesPanel)d).UpdateHeight();

    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (ValuesPanel)d;
        if (e.OldValue is INotifyCollectionChanged oldCol) oldCol.CollectionChanged -= self.OnCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCol) newCol.CollectionChanged += self.OnCollectionChanged;
        self.UpdateHeight();
        self.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHeight();
        InvalidateVisual();
    }

    private void UpdateHeight() => Height = RulerHeight + (Rows?.Count ?? 0) * RowHeight;

    protected override void OnRender(DrawingContext dc)
    {
        double width = Math.Max(ActualWidth, 1);
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(255, 26, 28, 32)), null, new Rect(0, 0, width, RulerHeight));
        var header = new FormattedText("Values", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 11, HeaderBrush, 1.0);
        dc.DrawText(header, new Point(8, (RulerHeight - header.Height) / 2));

        var rows = Rows;
        if (rows == null) return;

        for (int i = 0; i < rows.Count; i++)
        {
            double y = RulerHeight + i * RowHeight;
            dc.DrawLine(GridPen, new Point(0, y), new Point(width, y));

            var node = rows[i];
            string raw = SignalValueLookup.GetValueAt(node.Variable, CursorSeconds) ?? "-";
            string display = node.Variable == null ? "-" : SignalValueLookup.FormatValue(raw, node.Variable.Width, node.Variable.Type);

            var text = new FormattedText(display, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Consolas"), 12, TextBrush, 1.0);
            dc.DrawText(text, new Point(8, y + (RowHeight - text.Height) / 2));
        }
    }
}
