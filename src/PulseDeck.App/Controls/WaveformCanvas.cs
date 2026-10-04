using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PulseDeck.App;
using PulseDeck.App.ViewModels;
using PulseDeck.Core.Model;
using PulseDeck.Core.Vcd;

namespace PulseDeck.App.Controls;

/// <summary>
/// Renders one waveform row per entry in <see cref="Rows"/> — a flat list of signals only, no
/// file/scope separators (those live in the browser tree instead). Owns zoom/pan/cursor interaction,
/// plus markup-tool placement (vertical lines, text, arrows) when <see cref="ActiveTool"/> is not Select.
/// </summary>
public sealed class WaveformCanvas : FrameworkElement
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(System.Collections.ObjectModel.ObservableCollection<SignalTreeNode>), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnRowsChanged));

    public static readonly DependencyProperty AnnotationsProperty = DependencyProperty.Register(
        nameof(Annotations), typeof(System.Collections.ObjectModel.ObservableCollection<Annotation>), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnAnnotationsChanged));

    public static readonly DependencyProperty WindowStartSecondsProperty = DependencyProperty.Register(
        nameof(WindowStartSeconds), typeof(double), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty WindowEndSecondsProperty = DependencyProperty.Register(
        nameof(WindowEndSeconds), typeof(double), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(0.001, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CursorSecondsProperty = DependencyProperty.Register(
        nameof(CursorSeconds), typeof(double), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty RulerHeightProperty = DependencyProperty.Register(
        nameof(RulerHeight), typeof(double), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure, OnLayoutAffectingChanged));

    public static readonly DependencyProperty RulerFontSizeProperty = DependencyProperty.Register(
        nameof(RulerFontSize), typeof(double), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(11.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RulerTextVerticalProperty = DependencyProperty.Register(
        nameof(RulerTextVertical), typeof(bool), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowGridProperty = DependencyProperty.Register(
        nameof(ShowGrid), typeof(bool), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ActiveToolProperty = DependencyProperty.Register(
        nameof(ActiveTool), typeof(MarkupTool), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(MarkupTool.Select));

    public static readonly DependencyProperty MaxWindowSpanSecondsProperty = DependencyProperty.Register(
        nameof(MaxWindowSpanSeconds), typeof(double), typeof(WaveformCanvas),
        new FrameworkPropertyMetadata(double.MaxValue));

    public System.Collections.ObjectModel.ObservableCollection<SignalTreeNode>? Rows
    {
        get => (System.Collections.ObjectModel.ObservableCollection<SignalTreeNode>?)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public System.Collections.ObjectModel.ObservableCollection<Annotation>? Annotations
    {
        get => (System.Collections.ObjectModel.ObservableCollection<Annotation>?)GetValue(AnnotationsProperty);
        set => SetValue(AnnotationsProperty, value);
    }

    public double WindowStartSeconds { get => (double)GetValue(WindowStartSecondsProperty); set => SetValue(WindowStartSecondsProperty, value); }
    public double WindowEndSeconds { get => (double)GetValue(WindowEndSecondsProperty); set => SetValue(WindowEndSecondsProperty, value); }
    public double CursorSeconds { get => (double)GetValue(CursorSecondsProperty); set => SetValue(CursorSecondsProperty, value); }
    public double RulerHeight { get => (double)GetValue(RulerHeightProperty); set => SetValue(RulerHeightProperty, value); }
    public double RulerFontSize { get => (double)GetValue(RulerFontSizeProperty); set => SetValue(RulerFontSizeProperty, value); }
    public bool RulerTextVertical { get => (bool)GetValue(RulerTextVerticalProperty); set => SetValue(RulerTextVerticalProperty, value); }
    public bool ShowGrid { get => (bool)GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public MarkupTool ActiveTool { get => (MarkupTool)GetValue(ActiveToolProperty); set => SetValue(ActiveToolProperty, value); }
    public double MaxWindowSpanSeconds { get => (double)GetValue(MaxWindowSpanSecondsProperty); set => SetValue(MaxWindowSpanSecondsProperty, value); }

    private const double RowHeight = MainViewModel.RowHeight;

    private static readonly Pen RulerTickPen = MakePen(Color.FromArgb(90, 255, 255, 255), 1);
    private static readonly Pen CursorPen = MakePen(Color.FromRgb(0xFF, 0xD5, 0x4F), 1.2);
    private static readonly Brush RulerTextBrush = new SolidColorBrush(Color.FromRgb(0xC8, 0xCC, 0xD4));

    private Point? _dragStart;
    private double _dragWindowStart;
    private double _dragWindowEnd;
    private bool _didDrag;
    private (double TimeSeconds, int Row)? _pendingArrowStart;
    private Point? _pendingArrowCurrentPixel;

    public WaveformCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    private static Pen MakePen(Color c, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(c), thickness);
        pen.Freeze();
        return pen;
    }

    private static void OnLayoutAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((WaveformCanvas)d).UpdateHeight();

    private static void OnRowsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (WaveformCanvas)d;
        if (e.OldValue is INotifyCollectionChanged oldCol) oldCol.CollectionChanged -= self.OnRowsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCol) newCol.CollectionChanged += self.OnRowsCollectionChanged;
        self.UpdateHeight();
        self.InvalidateVisual();
    }

    private static void OnAnnotationsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (WaveformCanvas)d;
        if (e.OldValue is INotifyCollectionChanged oldCol) oldCol.CollectionChanged -= self.OnAnnotationsCollectionChanged;
        if (e.NewValue is INotifyCollectionChanged newCol) newCol.CollectionChanged += self.OnAnnotationsCollectionChanged;
        self.InvalidateVisual();
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateHeight();
        InvalidateVisual();
    }

    private void OnAnnotationsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    private void UpdateHeight()
    {
        int count = Rows?.Count ?? 0;
        Height = RulerHeight + count * RowHeight;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double width = ActualWidth;
        double height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Math.Max(width, 1), Math.Max(height, 1)));
        if (width <= 0) return;

        double span = Math.Max(WindowEndSeconds - WindowStartSeconds, 1e-15);
        double pxPerSec = width / span;
        double start = WindowStartSeconds;

        double TimeToX(double t) => (t - start) * pxPerSec;

        DrawRuler(dc, width, span);

        var rows = Rows;
        var gridPen = MakePen(Color.FromArgb(28, 255, 255, 255), 1);
        var vgridPen = MakePen(Color.FromArgb(18, 255, 255, 255), 1);

        if (ShowGrid)
        {
            double tickStep = NiceStep(span / Math.Max(width / 110.0, 2));
            if (tickStep > 0)
            {
                double first = Math.Floor(WindowStartSeconds / tickStep) * tickStep;
                for (double t = first; t <= WindowStartSeconds + span + tickStep; t += tickStep)
                {
                    double x = TimeToX(t);
                    if (x < -1 || x > width + 1) continue;
                    dc.DrawLine(vgridPen, new Point(x, RulerHeight), new Point(x, height));
                }
            }
        }

        if (rows != null)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                double y = RulerHeight + i * RowHeight;
                if (ShowGrid) dc.DrawLine(gridPen, new Point(0, y), new Point(width, y));
                DrawSignalRow(dc, rows[i], y, width, TimeToX);
            }
        }

        DrawEndOfFileMarkers(dc, rows, width, height, TimeToX);

        DrawAnnotations(dc, width, height, TimeToX, rows?.Count ?? 0);

        if (_pendingArrowStart is { } pending && _pendingArrowCurrentPixel is { } cur)
        {
            double sx = TimeToX(pending.TimeSeconds);
            double sy = RulerHeight + pending.Row * RowHeight + RowHeight / 2;
            DrawArrowShape(dc, new Point(sx, sy), cur, Brushes.Orange, null);
        }

        double cx = TimeToX(CursorSeconds);
        if (cx >= 0 && cx <= width)
            dc.DrawLine(CursorPen, new Point(cx, 0), new Point(cx, height));
    }

    private void DrawRuler(DrawingContext dc, double width, double span)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(255, 26, 28, 32)), null, new Rect(0, 0, width, RulerHeight));
        dc.DrawLine(RulerTickPen, new Point(0, RulerHeight), new Point(width, RulerHeight));

        double pxPerSec = width / span;
        double targetTicks = Math.Max(width / (RulerTextVertical ? 60.0 : 110.0), 2);
        double rawStep = span / targetTicks;
        double step = NiceStep(rawStep);
        if (step <= 0) return;

        var (unitLabel, unitScale) = PickUnit(span);
        var typeface = new Typeface("Segoe UI");

        double firstTick = Math.Floor(WindowStartSeconds / step) * step;
        for (double t = firstTick; t <= WindowStartSeconds + span + step; t += step)
        {
            double x = (t - WindowStartSeconds) * pxPerSec;
            if (x < -5 || x > width + 5) continue;
            dc.DrawLine(RulerTickPen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));
            string label = $"{t * unitScale:0.###} {unitLabel}";
            var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, typeface, RulerFontSize, RulerTextBrush, 1.0);

            if (RulerTextVertical)
            {
                dc.PushTransform(new RotateTransform(-90, x + 3, RulerHeight - 4));
                dc.DrawText(text, new Point(x + 3, RulerHeight - 4 - text.Height));
                dc.Pop();
            }
            else
            {
                dc.DrawText(text, new Point(x + 3, RulerHeight - text.Height - 2));
            }
        }
    }

    private static (string unit, double scale) PickUnit(double spanSeconds)
    {
        if (spanSeconds >= 1) return ("s", 1);
        if (spanSeconds >= 1e-3) return ("ms", 1e3);
        if (spanSeconds >= 1e-6) return ("us", 1e6);
        if (spanSeconds >= 1e-9) return ("ns", 1e9);
        return ("ps", 1e12);
    }

    private static double NiceStep(double roughStep)
    {
        if (roughStep <= 0) return 0;
        double exponent = Math.Floor(Math.Log10(roughStep));
        double fraction = roughStep / Math.Pow(10, exponent);
        double niceFraction = fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10;
        return niceFraction * Math.Pow(10, exponent);
    }

    private void DrawSignalRow(DrawingContext dc, SignalTreeNode node, double y, double width, Func<double, double> timeToX)
    {
        double opacity = node.IsMissing ? 0.35 : 1.0;
        var variable = node.Variable;
        var color = node.Color ?? new RgbColor(0x4F, 0xC3, 0xF7);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B));
        var pen = new Pen(brush, 1.6);

        if (node.IsNew)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 129, 199, 132)), null, new Rect(0, y, width, RowHeight));

        if (variable == null || variable.Changes.Count == 0)
        {
            var dashed = new Pen(brush, 1) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) };
            double midY = y + RowHeight / 2;
            dc.DrawLine(dashed, new Point(0, midY), new Point(width, midY));
            return;
        }

        // Never draw past this signal's own file's last recorded timestamp — beyond that point there is
        // no captured data, so a flat continuation would misleadingly suggest there is.
        double fileEnd = node.File?.Document?.EndTimeSeconds ?? double.MaxValue;
        double clipEndSeconds = Math.Min(WindowEndSeconds, fileEnd);
        if (clipEndSeconds < WindowStartSeconds) return;

        if (variable.Width <= 1)
            DrawScalarRow(dc, variable, y, width, timeToX, pen, brush, clipEndSeconds);
        else
            DrawVectorRow(dc, variable, y, width, timeToX, brush, opacity, clipEndSeconds);
    }

    /// <summary>
    /// Renders one scalar signal by aggregating per screen-pixel-column, not by skip-decimating raw
    /// transitions. A column that only ever holds one level over its time-slice draws a flat segment at
    /// that level; a column whose level changes within that same slice (more transitions than can be
    /// resolved at this zoom) draws a solid bar spanning the full high-to-low range instead.
    ///
    /// This replaces an earlier approach that skipped any transition landing within ~0.6px of the last
    /// *drawn* point — which transitions survived depended on exactly where column boundaries happened
    /// to fall relative to the data, so a fast clock could render as flat-0, flat-1, or a comb pattern
    /// depending on sub-pixel pan position, flickering between them as the view moved. Aggregating per
    /// column is a pure function of (zoom, pan, data): panning shifts which data falls in which column,
    /// but never which columns are "dense" vs "flat" without an actual change in what's visible, so an
    /// isolated short pulse still always shows (as a 1-column solid bar) rather than being skipped.
    /// </summary>
    private void DrawScalarRow(DrawingContext dc, VcdVariable variable, double y, double width,
        Func<double, double> timeToX, Pen pen, Brush brush, double clipEndSeconds)
    {
        var changes = variable.Changes;
        double highY = y + RowHeight * 0.22;
        double lowY = y + RowHeight * 0.78;
        double midY = y + RowHeight * 0.5;

        double YFor(string v) => v switch { "1" => highY, "0" => lowY, _ => midY };

        double span = WindowEndSeconds - WindowStartSeconds;
        if (span <= 0 || width <= 0) return;
        double secPerPixel = span / width;

        int startIdx = Math.Max(0, SignalValueLookup.FindFirstIndexAtOrAfter(changes, WindowStartSeconds) - 1);
        if (startIdx >= changes.Count || changes[startIdx].TimeSeconds > clipEndSeconds) return;

        int lastCol = (int)Math.Ceiling(Math.Min(width, (clipEndSeconds - WindowStartSeconds) / secPerPixel));
        if (lastCol <= 0) return;

        string currentValue = changes[startIdx].Value;
        int idx = startIdx + 1;

        var fillBrush = new SolidColorBrush(((SolidColorBrush)brush).Color) { Opacity = 0.75 };

        var geo = new StreamGeometry();
        bool figureOpen = false;

        void LineTo(double x, double yVal, StreamGeometryContext ctx)
        {
            if (!figureOpen) { ctx.BeginFigure(new Point(x, yVal), false, false); figureOpen = true; }
            else ctx.LineTo(new Point(x, yVal), true, false);
        }

        using (var ctx = geo.Open())
        {
            int runStartCol = 0;
            bool runIsDense = false;
            string runFlatValue = currentValue;
            bool sawHighLevel = false, sawLowLevel = false, sawMidLevel = false;

            void NoteLevel(string v)
            {
                switch (v) { case "1": sawHighLevel = true; break; case "0": sawLowLevel = true; break; default: sawMidLevel = true; break; }
            }

            void FlushFlat(int startCol, int endCol, string val)
            {
                if (endCol <= startCol) return;
                double yVal = YFor(val);
                LineTo(startCol, yVal, ctx);
                LineTo(endCol, yVal, ctx);
            }

            void FlushDense(int startCol, int endCol)
            {
                if (endCol <= startCol) return;
                dc.DrawRectangle(fillBrush, null, new Rect(startCol, highY, endCol - startCol, lowY - highY));
                figureOpen = false; // the filled block already spans both rails — start the next flat run fresh
            }

            NoteLevel(currentValue);

            for (int col = 0; col < lastCol; col++)
            {
                double colEndTime = WindowStartSeconds + (col + 1) * secPerPixel;

                while (idx < changes.Count && changes[idx].TimeSeconds < colEndTime && changes[idx].TimeSeconds <= clipEndSeconds)
                {
                    currentValue = changes[idx].Value;
                    NoteLevel(currentValue);
                    idx++;
                }

                bool dense = (sawHighLevel ? 1 : 0) + (sawLowLevel ? 1 : 0) + (sawMidLevel ? 1 : 0) > 1;

                if (col == runStartCol)
                {
                    runIsDense = dense;
                    runFlatValue = currentValue;
                }
                else if (dense != runIsDense || (!dense && currentValue != runFlatValue))
                {
                    if (runIsDense) FlushDense(runStartCol, col); else FlushFlat(runStartCol, col, runFlatValue);
                    runStartCol = col;
                    runIsDense = dense;
                    runFlatValue = currentValue;
                }

                // Each column's level set starts fresh except for the value carried into the next one.
                sawHighLevel = sawLowLevel = sawMidLevel = false;
                NoteLevel(currentValue);
            }

            if (runIsDense) FlushDense(runStartCol, lastCol); else FlushFlat(runStartCol, lastCol, runFlatValue);
        }

        if (figureOpen)
        {
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }

    private void DrawVectorRow(DrawingContext dc, VcdVariable variable, double y, double width,
        Func<double, double> timeToX, Brush brush, double opacity, double clipEndSeconds)
    {
        var changes = variable.Changes;
        double top = y + RowHeight * 0.15;
        double bottom = y + RowHeight * 0.85;
        var pen = new Pen(brush, 1.2);
        var fill = new SolidColorBrush(Color.FromArgb((byte)(opacity * 60), 100, 181, 246));

        double clipX = Math.Min(width, timeToX(clipEndSeconds));

        int startIdx = Math.Max(0, SignalValueLookup.FindFirstIndexAtOrAfter(changes, WindowStartSeconds) - 1);
        int idx = startIdx;
        int guard = 0;
        while (idx < changes.Count && changes[idx].TimeSeconds <= clipEndSeconds && guard++ < 20000)
        {
            double segStartX = Math.Max(timeToX(changes[idx].TimeSeconds), 0);
            double segEndX = idx + 1 < changes.Count ? timeToX(changes[idx + 1].TimeSeconds) : clipX;
            segEndX = Math.Min(segEndX, clipX);

            if (segEndX > segStartX)
            {
                var rect = new Rect(segStartX, top, Math.Max(segEndX - segStartX, 1), bottom - top);
                dc.DrawRectangle(fill, pen, rect);
                if (segEndX - segStartX > 26)
                {
                    string label = SignalValueLookup.FormatValue(changes[idx].Value, variable.Width, variable.Type);
                    var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Consolas"), 10, RulerTextBrush, 1.0);
                    if (text.Width < segEndX - segStartX - 4)
                        dc.DrawText(text, new Point(segStartX + (segEndX - segStartX - text.Width) / 2, y + (RowHeight - text.Height) / 2));
                }
            }
            idx++;
        }
    }

    // ---- End-of-file markers ----

    private static readonly Pen EndOfFilePen = MakePen(Color.FromArgb(150, 0xB0, 0x41, 0x3E), 1.2);
    private static readonly Brush EndOfFileBrush = new SolidColorBrush(Color.FromArgb(200, 0xD8, 0x8A, 0x87));

    /// <summary>Marks the last timestamp of each distinct file among the currently-displayed rows with
    /// a vertical line and a downward-reading "END OF FILE" label — useful once several captures of
    /// different lengths are aggregated in one view, so it's clear where each one's data actually ends.</summary>
    private void DrawEndOfFileMarkers(DrawingContext dc, System.Collections.ObjectModel.ObservableCollection<SignalTreeNode>? rows,
        double width, double height, Func<double, double> timeToX)
    {
        if (rows == null || rows.Count == 0) return;

        var seenEndTimes = new HashSet<double>();
        foreach (var row in rows)
        {
            var doc = row.File?.Document;
            if (doc == null) continue;
            double endTime = doc.EndTimeSeconds;
            if (!seenEndTimes.Add(endTime)) continue; // multiple files ending at the same time share one marker

            double x = timeToX(endTime);
            if (x < -2 || x > width + 2) continue;

            var dashed = new Pen(EndOfFilePen.Brush, EndOfFilePen.Thickness) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) };
            dc.DrawLine(dashed, new Point(x, RulerHeight), new Point(x, height));

            var text = new FormattedText("END OF FILE", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 10.5, EndOfFileBrush, 1.0);
            dc.PushTransform(new RotateTransform(90, x + 4, RulerHeight + 4));
            dc.DrawText(text, new Point(x + 4, RulerHeight + 4));
            dc.Pop();
        }
    }

    // ---- Markup annotations ----

    private void DrawAnnotations(DrawingContext dc, double width, double height, Func<double, double> timeToX, int rowCount)
    {
        var annotations = Annotations;
        if (annotations == null) return;

        foreach (var a in annotations)
        {
            var color = (Color)ColorConverter.ConvertFromString(a.ColorHex)!;
            var brush = new SolidColorBrush(color);
            var pen = new Pen(brush, 1.4);

            switch (a.Kind)
            {
                case AnnotationKind.VerticalLine:
                {
                    double x = timeToX(a.TimeSeconds);
                    if (x < -2 || x > width + 2) break;
                    dc.DrawLine(pen, new Point(x, RulerHeight), new Point(x, height));
                    if (!string.IsNullOrEmpty(a.Text))
                        DrawLabelChip(dc, a.Text, new Point(x + 3, RulerHeight + 2), brush);
                    break;
                }
                case AnnotationKind.Text:
                {
                    double x = timeToX(a.TimeSeconds);
                    double y = RulerHeight + a.RowIndex * RowHeight + RowHeight / 2;
                    dc.DrawEllipse(brush, null, new Point(x, y), 3, 3);
                    DrawLabelChip(dc, a.Text, new Point(x + 6, y - 9), brush);
                    break;
                }
                case AnnotationKind.Arrow:
                {
                    double x1 = timeToX(a.TimeSeconds);
                    double y1 = RulerHeight + a.RowIndex * RowHeight + RowHeight / 2;
                    double x2 = timeToX(a.TimeSeconds2);
                    double y2 = RulerHeight + a.RowIndex2 * RowHeight + RowHeight / 2;
                    double deltaSeconds = Math.Abs(a.TimeSeconds2 - a.TimeSeconds);
                    string label = string.IsNullOrEmpty(a.Text) ? FormatDelta(deltaSeconds) : $"{a.Text} (Δ{FormatDelta(deltaSeconds)})";
                    DrawArrowShape(dc, new Point(x1, y1), new Point(x2, y2), brush, label);
                    break;
                }
            }
        }
    }

    private void DrawArrowShape(DrawingContext dc, Point from, Point to, Brush brush, string? label)
    {
        var pen = new Pen(brush, 1.6);
        dc.DrawLine(pen, from, to);

        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double headLen = 9;
        const double headAngle = Math.PI / 7;
        var p1 = new Point(to.X - headLen * Math.Cos(angle - headAngle), to.Y - headLen * Math.Sin(angle - headAngle));
        var p2 = new Point(to.X - headLen * Math.Cos(angle + headAngle), to.Y - headLen * Math.Sin(angle + headAngle));
        var headGeo = new StreamGeometry();
        using (var ctx = headGeo.Open())
        {
            ctx.BeginFigure(to, true, true);
            ctx.LineTo(p1, true, false);
            ctx.LineTo(p2, true, false);
        }
        headGeo.Freeze();
        dc.DrawGeometry(brush, null, headGeo);

        if (!string.IsNullOrEmpty(label))
        {
            var mid = new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2 - 14);
            DrawLabelChip(dc, label, mid, brush);
        }
    }

    private static void DrawLabelChip(DrawingContext dc, string text, Point topLeft, Brush accent)
    {
        var typeface = new Typeface("Segoe UI");
        var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, 10.5, Brushes.White, 1.0);
        var rect = new Rect(topLeft.X, topLeft.Y, ft.Width + 8, ft.Height + 4);
        var bg = new SolidColorBrush(Color.FromArgb(210, 30, 32, 36));
        dc.DrawRoundedRectangle(bg, new Pen(accent, 1), rect, 3, 3);
        dc.DrawText(ft, new Point(topLeft.X + 4, topLeft.Y + 2));
    }

    private static string FormatDelta(double seconds)
    {
        double a = Math.Abs(seconds);
        if (a >= 1) return $"{seconds:0.###} s";
        if (a >= 1e-3) return $"{seconds * 1e3:0.###} ms";
        if (a >= 1e-6) return $"{seconds * 1e6:0.###} us";
        if (a >= 1e-9) return $"{seconds * 1e9:0.###} ns";
        return $"{seconds * 1e12:0.###} ps";
    }

    /// <summary>Snaps a raw click time to the nearest actual transition among the displayed signals,
    /// if one falls within a small pixel tolerance — so markers land on real sample points.</summary>
    private double SnapToNearestTransition(double rawTimeSeconds, double pxPerSec)
    {
        var rows = Rows;
        if (rows == null || rows.Count == 0) return rawTimeSeconds;

        double toleranceSeconds = 6.0 / Math.Max(pxPerSec, 1e-15);
        double best = rawTimeSeconds;
        double bestDist = toleranceSeconds;

        foreach (var row in rows)
        {
            var changes = row.Variable?.Changes;
            if (changes == null || changes.Count == 0) continue;
            int idx = SignalValueLookup.FindFirstIndexAtOrAfter(changes, rawTimeSeconds);
            for (int cand = idx - 1; cand <= idx; cand++)
            {
                if (cand < 0 || cand >= changes.Count) continue;
                double dist = Math.Abs(changes[cand].TimeSeconds - rawTimeSeconds);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = changes[cand].TimeSeconds;
                }
            }
        }
        return best;
    }

    private int RowIndexFromY(double y)
    {
        int count = Rows?.Count ?? 0;
        if (count == 0) return 0;
        int idx = (int)Math.Floor((y - RulerHeight) / RowHeight);
        return Math.Clamp(idx, 0, count - 1);
    }

    // ---- Mouse interaction ----

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        double factor = e.Delta > 0 ? 0.85 : 1.0 / 0.85;
        Point pos = e.GetPosition(this);
        double span = WindowEndSeconds - WindowStartSeconds;
        double pivotTime = WindowStartSeconds + (pos.X / Math.Max(ActualWidth, 1)) * span;
        double newSpan = Math.Max(span * factor, 1e-15);
        if (factor > 1.0) newSpan = Math.Min(newSpan, MaxWindowSpanSeconds); // zooming out — don't pass the 1/3-screen floor
        double newStart = pivotTime - (pivotTime - WindowStartSeconds) * (newSpan / span);
        WindowStartSeconds = newStart;
        WindowEndSeconds = newStart + newSpan;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Point pos = e.GetPosition(this);
        double span = WindowEndSeconds - WindowStartSeconds;
        double pxPerSec = Math.Max(ActualWidth, 1) / span;
        double rawTime = WindowStartSeconds + (pos.X / Math.Max(ActualWidth, 1)) * span;

        if (ActiveTool == MarkupTool.Select)
        {
            CaptureMouse();
            _dragStart = pos;
            _dragWindowStart = WindowStartSeconds;
            _dragWindowEnd = WindowEndSeconds;
            _didDrag = false;
            return;
        }

        double snapped = SnapToNearestTransition(rawTime, pxPerSec);
        int row = RowIndexFromY(pos.Y);

        switch (ActiveTool)
        {
            case MarkupTool.VerticalLine:
            {
                string? label = TextInputDialog.Show(Window.GetWindow(this), "Label (optional):");
                if (label == null) return;
                Annotations?.Add(new Annotation { Kind = AnnotationKind.VerticalLine, TimeSeconds = snapped, Text = label });
                break;
            }
            case MarkupTool.Text:
            {
                string? label = TextInputDialog.Show(Window.GetWindow(this), "Markup text:");
                if (string.IsNullOrEmpty(label)) return;
                Annotations?.Add(new Annotation { Kind = AnnotationKind.Text, TimeSeconds = snapped, RowIndex = row, Text = label });
                break;
            }
            case MarkupTool.Arrow:
                CaptureMouse();
                _pendingArrowStart = (snapped, row);
                _pendingArrowCurrentPixel = pos;
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_pendingArrowStart != null)
        {
            _pendingArrowCurrentPixel = e.GetPosition(this);
            InvalidateVisual();
            return;
        }

        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;

        Point pos = e.GetPosition(this);
        double dx = pos.X - start.X;
        if (Math.Abs(dx) > 2) _didDrag = true;

        double span = _dragWindowEnd - _dragWindowStart;
        double secondsPerPixel = span / Math.Max(ActualWidth, 1);
        double shift = -dx * secondsPerPixel;
        WindowStartSeconds = _dragWindowStart + shift;
        WindowEndSeconds = _dragWindowEnd + shift;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();

        if (_pendingArrowStart is { } startPoint)
        {
            Point pos = e.GetPosition(this);
            double span = WindowEndSeconds - WindowStartSeconds;
            double pxPerSec = Math.Max(ActualWidth, 1) / span;
            double rawTime = WindowStartSeconds + (pos.X / Math.Max(ActualWidth, 1)) * span;
            double snapped = SnapToNearestTransition(rawTime, pxPerSec);
            int row = RowIndexFromY(pos.Y);

            _pendingArrowStart = null;
            _pendingArrowCurrentPixel = null;

            if (Math.Abs(snapped - startPoint.TimeSeconds) > 1e-15 || row != startPoint.Row)
            {
                string? label = TextInputDialog.Show(Window.GetWindow(this), "Arrow label (optional):");
                if (label != null)
                {
                    Annotations?.Add(new Annotation
                    {
                        Kind = AnnotationKind.Arrow,
                        TimeSeconds = startPoint.TimeSeconds,
                        RowIndex = startPoint.Row,
                        TimeSeconds2 = snapped,
                        RowIndex2 = row,
                        Text = label
                    });
                }
            }
            InvalidateVisual();
            return;
        }

        if (!_didDrag && ActiveTool == MarkupTool.Select)
        {
            Point pos = e.GetPosition(this);
            double span = WindowEndSeconds - WindowStartSeconds;
            CursorSeconds = WindowStartSeconds + (pos.X / Math.Max(ActualWidth, 1)) * span;
        }
        _dragStart = null;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        var annotations = Annotations;
        if (annotations == null || annotations.Count == 0) return;

        Point pos = e.GetPosition(this);
        double span = WindowEndSeconds - WindowStartSeconds;
        double width = Math.Max(ActualWidth, 1);
        double TimeToX(double t) => (t - WindowStartSeconds) * (width / span);

        const double tol = 8.0;
        Annotation? hit = null;
        foreach (var a in annotations)
        {
            switch (a.Kind)
            {
                case AnnotationKind.VerticalLine:
                    if (Math.Abs(TimeToX(a.TimeSeconds) - pos.X) < tol) hit = a;
                    break;
                case AnnotationKind.Text:
                {
                    double x = TimeToX(a.TimeSeconds);
                    double y = RulerHeight + a.RowIndex * RowHeight + RowHeight / 2;
                    if (Math.Abs(x - pos.X) < tol * 3 && Math.Abs(y - pos.Y) < tol) hit = a;
                    break;
                }
                case AnnotationKind.Arrow:
                {
                    double x1 = TimeToX(a.TimeSeconds), y1 = RulerHeight + a.RowIndex * RowHeight + RowHeight / 2;
                    double x2 = TimeToX(a.TimeSeconds2), y2 = RulerHeight + a.RowIndex2 * RowHeight + RowHeight / 2;
                    if (DistanceToSegment(pos, new Point(x1, y1), new Point(x2, y2)) < tol) hit = a;
                    break;
                }
            }
            if (hit != null) break;
        }

        if (hit != null)
        {
            annotations.Remove(hit);
            e.Handled = true;
        }
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-9) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        var proj = new Point(a.X + t * dx, a.Y + t * dy);
        return (p - proj).Length;
    }
}
