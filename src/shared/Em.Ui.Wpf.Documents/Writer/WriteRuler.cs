// Ruler editing follows WordCraft (crates/ui-egui/src/ruler_tabs.rs, canvas/rulers.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Em.Editing;
using Em.Write.Model;

namespace Em.Ui.Wpf.Documents.Writer;

/// <summary>
/// The horizontal ruler. In Full mode it edits the caret paragraph like a word processor: the box at its left picks the
/// kind of tab stop, a click over the text column sets one, dragging a stop moves it, dragging it off the ruler clears it,
/// the indent markers drag the first-line, left and right indents, and the edges of the text column drag the margins.
/// A gesture is applied once, when the button is released, so it is one undo step.
/// </summary>
internal sealed class WriteRuler : FrameworkElement
{
    private const double Top = 4, Bottom = 23;
    // How far past the ruler, vertically, the pointer takes a dragged tab stop off it.
    private const double DropOff = 18;
    // Indents and tab stops snap to a sixteenth of an inch unless Alt is held.
    private const double Snap = 6;
    private static readonly WriteTabAlignment[] Kinds =
        [WriteTabAlignment.Left, WriteTabAlignment.Center, WriteTabAlignment.Right, WriteTabAlignment.Decimal, WriteTabAlignment.Bar];
    private static readonly Rect Selector = new(4, 5, 16, 16);

    private readonly MatrixWriter owner;
    private WriteTabAlignment kind = WriteTabAlignment.Left;
    private Drag? drag;

    private enum Target { FirstLine, Left, Right, Tab, NewTab, MarginLeft, MarginRight }

    private sealed record Drag(Target Target, double From, double At, bool Off, WriteTabAlignment Alignment);

    private readonly record struct Geometry(WritePageSettings Page, double PageLeft, double Zoom, double ColumnLeft, double ColumnWidth,
        WriteParagraphStyle? Format)
    {
        public double X(double position) => ColumnLeft + position * Zoom;
        public double Position(double x) => (x - ColumnLeft) / Zoom;
    }

    internal WriteRuler(MatrixWriter owner)
    {
        this.owner = owner;
        Height = 28;
        ClipToBounds = true;
        Focusable = false;
    }

    /// <summary>The kind of tab stop a click on the ruler sets.</summary>
    internal WriteTabAlignment TabKind => kind;

    // The palette is swapped when the inherited theme mode changes; repaint once the new brushes are in place.
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == global::Em.Ui.Wpf.Documents.Themes.GridTheme.ModeProperty)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(InvalidateVisual));
    }

    private Geometry Measure()
    {
        var session = owner.Session;
        var page = session.Document.Sections.ElementAtOrDefault(session.Selection.Caret.Section)?.Page ?? new WritePageSettings();
        double zoom = owner.Zoom;
        double width = page.Width * zoom;
        double left = owner.PageScreenLeft(width) ?? Math.Max(24, (ActualWidth - width) / 2);
        var paragraph = session.GetParagraph(session.Selection.Caret);
        var format = paragraph is null ? null : session.Document.Resolve(paragraph).Paragraph;
        return new(page, left, zoom, left + page.Left * zoom, page.Width - page.Left - page.Right, format);
    }

    // The ruler edits the page's text column; inside a table the column is the cell, which the ruler does not show.
    private bool Editable => owner.Mode == EditorMode.Full && owner.Session.Selection.Caret.Row < 0 &&
        owner.Session.GetParagraph(owner.Session.Selection.Caret) is not null;

    protected override void OnRender(DrawingContext drawing)
    {
        var g = Measure();
        var ruler = new Rect(g.PageLeft, Top, g.Page.Width * g.Zoom, Bottom - Top);
        drawing.DrawRectangle(Brush("SurfaceContainerLow"), null, new Rect(RenderSize));
        drawing.DrawRectangle(Brush("SurfaceContainerHigh"), null, ruler);
        double columnLeft = g.ColumnLeft, columnWidth = g.ColumnWidth;
        if (drag?.Target is Target.MarginLeft)
            (columnLeft, columnWidth) = (g.PageLeft + drag.At * g.Zoom, g.Page.Width - drag.At - g.Page.Right);
        else if (drag?.Target is Target.MarginRight)
            columnWidth = g.Page.Width - g.Page.Left - drag.At;
        drawing.DrawRectangle(Brush("SurfaceContainerLowest"), null,
            new Rect(columnLeft, Top, Math.Max(0, columnWidth * g.Zoom), Bottom - Top));
        var ink = Brush("OnSurfaceVariant");
        var pen = new Pen(Brush("Outline"), 1);
        // Numbers keep clear of the indent markers and tab stops, as in WordCraft; a tick stands in for a covered number.
        var markers = new List<double>();
        if (g.Format is { } marked)
        {
            double indent = marked.LeftIndent ?? 0;
            markers.AddRange([g.X(indent), g.X(indent + (marked.FirstLineIndent ?? 0)), g.X(g.ColumnWidth - (marked.RightIndent ?? 0))]);
            markers.AddRange((marked.TabStops ?? []).Select(stop => g.X(stop.Position)));
        }
        for (int i = 0; i * 12 < g.Page.Width; i++)
        {
            double tick = g.PageLeft + i * 12 * g.Zoom;
            if (i % 8 == 0 && g.Zoom >= .7 && !markers.Any(marker => Math.Abs(marker - tick) < 9))
            {
                var text = new FormattedText((i / 8).ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                drawing.DrawText(text, new(tick - text.Width / 2, 5));
            }
            else
                drawing.DrawLine(pen, new(tick, Bottom - (i % 4 == 0 ? 6 : 3)), new(tick, Bottom));
        }

        if (g.Format is { } format)
        {
            var primary = Brush("Primary");
            var stopPen = new Pen(Brush("OnSurface"), 1.5);
            foreach (var stop in format.TabStops ?? [])
                if (drag is not { Target: Target.Tab } moving || Math.Abs(moving.From - stop.Position) > WriteTabStop.Tolerance)
                    TabGlyph(drawing, g.X(stop.Position), stop.Alignment, stopPen);
            if (drag is { Target: Target.Tab or Target.NewTab, Off: false } placing)
                TabGlyph(drawing, g.X(placing.At), placing.Alignment, new Pen(primary, 1.5));

            double left = format.LeftIndent ?? 0, first = left + (format.FirstLineIndent ?? 0), right = g.ColumnWidth - (format.RightIndent ?? 0);
            switch (drag?.Target)
            {
                case Target.FirstLine: first = drag.At; break;
                case Target.Left: first += drag.At - left; left = drag.At; break;
                case Target.Right: right = drag.At; break;
            }
            Marker(drawing, g.X(left), false, primary);
            Marker(drawing, g.X(first), true, primary);
            Marker(drawing, g.X(right), false, primary);
        }

        if (owner.Mode == EditorMode.Full)
        {
            drawing.DrawRoundedRectangle(Brush("SurfaceContainerLow"), new Pen(Brush("OutlineVariant"), 1), Selector, 3, 3);
            TabGlyph(drawing, Selector.Left + Selector.Width / 2 + (kind switch
            {
                WriteTabAlignment.Left => -2, WriteTabAlignment.Right => 2, _ => 0
            }), kind, new Pen(Brush("OnSurface"), 1.5), Selector.Bottom - 4);
        }
    }

    private static void Marker(DrawingContext drawing, double x, bool above, Brush brush)
    {
        var triangle = new StreamGeometry();
        using (var shape = triangle.Open())
        {
            double y = above ? 3 : Bottom;
            shape.BeginFigure(new(x - 5, y), true, true);
            shape.LineTo(new(x + 5, y), true, false);
            shape.LineTo(new(x, y + (above ? 6 : -6)), true, false);
        }
        triangle.Freeze();
        drawing.DrawGeometry(brush, null, triangle);
    }

    // A stop's marker standing on the ruler's base: a stem with a foot showing which way the text runs from it.
    private static void TabGlyph(DrawingContext drawing, double x, WriteTabAlignment alignment, Pen pen, double baseline = Bottom - 1)
    {
        drawing.DrawLine(pen, new(x, baseline - (alignment == WriteTabAlignment.Bar ? 7 : 5)), new(x, baseline));
        switch (alignment)
        {
            case WriteTabAlignment.Left: drawing.DrawLine(pen, new(x, baseline), new(x + 4, baseline)); break;
            case WriteTabAlignment.Right: drawing.DrawLine(pen, new(x - 4, baseline), new(x, baseline)); break;
            case WriteTabAlignment.Center: drawing.DrawLine(pen, new(x - 4, baseline), new(x + 4, baseline)); break;
            case WriteTabAlignment.Decimal:
                drawing.DrawLine(pen, new(x - 4, baseline), new(x + 4, baseline));
                drawing.DrawEllipse(pen.Brush, null, new(x + 3, baseline - 3.5), 1.1, 1.1);
                break;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonDown(args);
        var point = args.GetPosition(this);
        if (owner.Mode == EditorMode.Full && Selector.Contains(point))
        {
            kind = Kinds[(Array.IndexOf(Kinds, kind) + 1) % Kinds.Length];
            InvalidateVisual();
            args.Handled = true;
            return;
        }
        if (!Editable || point.Y < 0 || point.Y > Bottom + 2)
            return;
        var g = Measure();
        var start = HitTest(g, point);
        if (start is null)
            return;
        drag = start;
        CaptureMouse();
        owner.ShowRulerGuide(GuideX(g, start));
        InvalidateVisual();
        args.Handled = true;
    }

    private Drag? HitTest(Geometry g, Point point)
    {
        var format = g.Format ?? new WriteParagraphStyle();
        double position = g.Position(point.X);
        double left = format.LeftIndent ?? 0, first = left + (format.FirstLineIndent ?? 0), right = g.ColumnWidth - (format.RightIndent ?? 0);
        // Indent markers win over stops and margins where they overlap, as in WordCraft. The band between the top and
        // bottom markers keeps the margin edge reachable when an indent sits on it.
        if (Near(g.X(first), point.X) && point.Y <= 10)
            return new(Target.FirstLine, first, first, false, kind);
        if (Near(g.X(left), point.X) && point.Y >= 16)
            return new(Target.Left, left, left, false, kind);
        if (Near(g.X(right), point.X) && point.Y >= 16)
            return new(Target.Right, right, right, false, kind);
        var stops = (format.TabStops ?? []).Where(stop => Near(g.X(stop.Position), point.X)).ToList();
        if (stops.Count > 0)
        {
            var stop = stops.MinBy(item => Math.Abs(g.X(item.Position) - point.X));
            return new(Target.Tab, stop.Position, stop.Position, false, stop.Alignment);
        }
        if (Near(g.ColumnLeft, point.X))
            return new(Target.MarginLeft, g.Page.Left, g.Page.Left, false, kind);
        if (Near(g.ColumnLeft + g.ColumnWidth * g.Zoom, point.X))
            return new(Target.MarginRight, g.Page.Right, g.Page.Right, false, kind);
        if (position > 0 && position < g.ColumnWidth)
            return new(Target.NewTab, double.NaN, SnapTo(position, g.ColumnWidth), false, kind);
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        var point = args.GetPosition(this);
        if (drag is null || !IsMouseCaptured)
        {
            Cursor = Editable && HitTest(Measure(), point) is { Target: not Target.NewTab } ? Cursors.SizeWE : null;
            return;
        }
        var g = Measure();
        double position = g.Position(point.X);
        drag = drag.Target switch
        {
            Target.Tab or Target.NewTab => drag with { At = SnapTo(position, g.ColumnWidth), Off = point.Y > Bottom + DropOff || point.Y < Top - DropOff },
            Target.FirstLine => drag with { At = SnapTo(position, g.ColumnWidth, true) },
            Target.Left => drag with { At = Math.Clamp(SnapTo(position, g.ColumnWidth, true), 0, g.ColumnWidth - 24) },
            Target.Right => drag with { At = Math.Clamp(SnapTo(position, g.ColumnWidth, true), 24, g.ColumnWidth) },
            // Margins are measured from the page edges and keep at least an inch of text column.
            Target.MarginLeft => drag with { At = Math.Clamp(Round((point.X - g.PageLeft) / g.Zoom), 0, g.Page.Width - g.Page.Right - 96) },
            _ => drag with { At = Math.Clamp(Round(g.Page.Width - (point.X - g.PageLeft) / g.Zoom), 0, g.Page.Width - g.Page.Left - 96) }
        };
        owner.ShowRulerGuide(GuideX(g, drag!));
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonUp(args);
        if (drag is not { } done)
            return;
        drag = null;
        owner.ShowRulerGuide(null);
        ReleaseMouseCapture();
        Apply(done);
        InvalidateVisual();
        args.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs args)
    {
        base.OnLostMouseCapture(args);
        owner.ShowRulerGuide(null);
        if (drag is null)
            return;
        drag = null;
        InvalidateVisual();
    }

    // Where the held marker lands in the text, as the line a word processor draws down the page while it is dragged.
    // A tab stop pulled off the ruler is being cleared, so it shows none.
    private static double? GuideX(Geometry g, Drag held) => held switch
    {
        { Off: true } => null,
        { Target: Target.MarginLeft } => g.PageLeft + held.At * g.Zoom,
        { Target: Target.MarginRight } => g.PageLeft + (g.Page.Width - held.At) * g.Zoom,
        _ => g.X(held.At)
    };

    private void Apply(Drag done)
    {
        var session = owner.Session;
        switch (done.Target)
        {
            case Target.NewTab when !done.Off:
                session.FormatParagraphs(style => style with { TabStops = SetStop(style.TabStops, null, new(done.At, done.Alignment)) });
                break;
            case Target.Tab when done.Off:
                session.FormatParagraphs(style => style with { TabStops = SetStop(style.TabStops, done.From, null) });
                break;
            case Target.Tab when Math.Abs(done.From - done.At) > WriteTabStop.Tolerance:
                session.FormatParagraphs(style => style with { TabStops = SetStop(style.TabStops, done.From, new(done.At, done.Alignment)) });
                break;
            case Target.FirstLine when Math.Abs(done.From - done.At) > .01:
                session.FormatParagraphs(style => style with { FirstLineIndent = done.At - (style.LeftIndent ?? 0) });
                break;
            case Target.Left when Math.Abs(done.From - done.At) > .01:
                session.FormatParagraphs(style => style with { LeftIndent = done.At });
                break;
            case Target.Right when Math.Abs(done.From - done.At) > .01:
                double column = Measure().ColumnWidth;
                session.FormatParagraphs(style => style with { RightIndent = Math.Max(0, column - done.At) });
                break;
            case Target.MarginLeft or Target.MarginRight when Math.Abs(done.From - done.At) > .01:
                var page = Measure().Page;
                session.SetPage(done.Target == Target.MarginLeft ? page with { Left = done.At } : page with { Right = done.At });
                break;
        }
    }

    /// <summary>A paragraph's stops with the stop at <paramref name="remove"/> cleared and <paramref name="add"/> set.</summary>
    internal static ImmutableArray<WriteTabStop> SetStop(ImmutableArray<WriteTabStop>? stops, double? remove, WriteTabStop? add)
    {
        var result = (stops ?? []).Where(stop => remove is not { } from || Math.Abs(stop.Position - from) > WriteTabStop.Tolerance).ToList();
        if (add is { } stop)
        {
            result.RemoveAll(item => Math.Abs(item.Position - stop.Position) <= WriteTabStop.Tolerance);
            result.Add(stop);
        }
        return [.. result.OrderBy(item => item.Position)];
    }

    private static bool Near(double marker, double x) => Math.Abs(marker - x) <= 5;

    private static double SnapTo(double position, double width, bool allowNegative = false)
    {
        double value = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? position : Math.Round(position / Snap) * Snap;
        return Math.Clamp(value, allowNegative ? -width : 0, width);
    }

    private static double Round(double value) => Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? value : Math.Round(value / Snap) * Snap;

    private Brush Brush(string role) => TryFindResource("MatrixGrid.Brush." + role) as Brush ?? Brushes.Transparent;
}
