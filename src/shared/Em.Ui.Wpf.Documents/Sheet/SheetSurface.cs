using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Em.Editing;
using Em.Sheet.Commands;
using Em.Sheet.Cells;
using Em.Sheet.Model;
using Em.Sheet.View;

namespace Em.Ui.Wpf.Documents.Sheet;

internal sealed class SheetSurface : FrameworkElement
{
    private readonly MatrixSheet owner;
    private CellAddress? anchor;
    private bool pointing;

    internal SheetSurface(MatrixSheet owner)
    {
        this.owner = owner;
        Focusable = true;
        ClipToBounds = true;
    }

    internal SheetViewport? Viewport { get; private set; }

    // The palette is swapped when the inherited theme mode changes; repaint once the new brushes are in place.
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == global::Em.Ui.Wpf.Documents.Themes.GridTheme.ModeProperty)
        {
            texts.Clear();
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(InvalidateVisual));
        }
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (!IsVisible && !owner.RenderOffscreen)
            return;
        base.OnRender(drawing);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var session = owner.Session;
        var viewport = SheetViewport.Create(session, owner.FirstRow, owner.FirstColumn, ActualWidth, ActualHeight, owner.Zoom,
            owner.RowOffset, owner.ColumnOffset);
        Viewport = viewport;
        owner.InvalidateCopyMark();
        long built = System.Diagnostics.Stopwatch.GetTimestamp();
        owner.UpdateScrollBars(viewport);
        long bars = System.Diagnostics.Stopwatch.GetTimestamp();
        if (owner.EditCell.HasValue)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(owner.PositionCellEditor));
        // Brushes and pens are resolved once per frame, not per cell.
        var header = Brush("SurfaceContainer");
        var background = Brush("SurfaceContainerLowest");
        var selected = Brush("StateSelected");
        var line = Frozen(new Pen(Brush("OutlineVariant"), 1));
        var primary = Brush("Primary");
        var activePen = Frozen(new Pen(primary, 2));
        var pointPen = Frozen(new Pen(primary, 2) { DashStyle = new DashStyle([2, 1.5], 0) });
        var handlePen = Frozen(new Pen(Brush("Surface"), 1));
        var dragPen = Frozen(new Pen(Brush("OnSurfaceVariant"), 2) { DashStyle = new DashStyle([3, 2], 0) });
        var dragPreview = DragPreview;
        var ink = Brush("OnSurface");
        var inkPen = Frozen(new Pen(ink, 1));
        var error = Brush("Error");
        var colors = colorBrushes;
        if (texts.Count > TextCacheLimit)
            texts.Clear();
        var selection = session.Selection;
        drawing.DrawRectangle(background, null, new Rect(RenderSize));

        // Up to four panes: frozen rows and columns, and the scrolled area. Each is painted under one clip.
        foreach (bool rowFrozen in new[] { true, false })
        foreach (bool columnFrozen in new[] { true, false })
        {
            double left = columnFrozen ? SheetViewport.RowHeaderWidth : viewport.ScrolledLeft;
            double right = columnFrozen ? viewport.ScrolledLeft : ActualWidth;
            double top = rowFrozen ? SheetViewport.ColumnHeaderHeight : viewport.ScrolledTop;
            double bottom = rowFrozen ? viewport.ScrolledTop : ActualHeight;
            if (right <= left || bottom <= top)
                continue;
            var pane = new Rect(left, top, right - left, bottom - top);
            drawing.PushClip(new RectangleGeometry(pane));
            var paneCells = viewport.Cells.Where(cell => Math.Abs(cell.Clip.X - left) < .01 && Math.Abs(cell.Clip.Y - top) < .01).ToList();

            // Fills, grid lines and borders are batched into one geometry per brush or pen: far zoomed out a frame holds
            // thousands of them, and one drawing instruction each would dominate the frame.
            var fills = new ShapeBatch();
            foreach (var cell in paneCells)
                if (cell.Merge is null && cell.Style.FillColor is { } fill)
                    fills.Rectangle(ColorBrush(colors, fill), Area(cell.Bounds));
            fills.Fill(drawing);
            if (SheetViewport.Span(viewport.Rows, selection.Start.Row, selection.End.Row, rowFrozen) is { } rowSpan &&
                SheetViewport.Span(viewport.Columns, selection.Start.Column, selection.End.Column, columnFrozen) is { } columnSpan)
                drawing.DrawRectangle(selected, null, new Rect(columnSpan.Start, rowSpan.Start,
                    columnSpan.End - columnSpan.Start, rowSpan.End - rowSpan.Start));
            // The active cell shows its own fill, not the selection tint.
            foreach (var cell in paneCells)
                if (cell.Active && cell.Merge is null && selection.Count > 1)
                    drawing.DrawRectangle(cell.Style.FillColor is { } fill ? ColorBrush(colors, fill) : background, null, Area(cell.Bounds));

            // Grid lines: one line per row and column edge instead of one rectangle per cell.
            var grid = new ShapeBatch();
            foreach (var row in viewport.Rows)
            {
                if (row.Frozen != rowFrozen)
                    continue;
                grid.Line(line, new(left, row.Start), new(right, row.Start));
                grid.Line(line, new(left, row.Start + row.Length), new(right, row.Start + row.Length));
            }
            foreach (var column in viewport.Columns)
            {
                if (column.Frozen != columnFrozen)
                    continue;
                grid.Line(line, new(column.Start, top), new(column.Start, bottom));
                grid.Line(line, new(column.Start + column.Length, top), new(column.Start + column.Length, bottom));
            }
            grid.Stroke(drawing);

            // Merged areas cover the grid lines inside them.
            foreach (var cell in paneCells)
            {
                if (cell.Merge is null)
                    continue;
                var area = Area(cell.Bounds);
                drawing.DrawRectangle(cell.Style.FillColor is { } fill ? ColorBrush(colors, fill) : background, line, area);
                if (cell.Selected && !cell.Active)
                    drawing.DrawRectangle(selected, null, area);
            }

            foreach (var cell in paneCells)
            {
                var area = Area(cell.Bounds);
                string text = owner.EditCell == cell.Address ? owner.EditText ?? "" : cell.Text;
                if (text.Length > 0 && CanDrawText(area, cell.Style))
                {
                    var alignment = cell.Style.Alignment switch
                    {
                        CellAlignment.Center => TextAlignment.Center, CellAlignment.End => TextAlignment.Right,
                        CellAlignment.Start => TextAlignment.Left, _ => session.GetValue(session.ActiveSheet, cell.Address).Kind ==
                            CellValueKind.Number ? TextAlignment.Right : TextAlignment.Left
                    };
                    DrawText(drawing, text, area, cell.Style.TextColor is { } color ? ColorBrush(colors, color) : ink, cell.Style, alignment);
                }
                if (cell.Error && area.Width >= 10 && area.Height >= 10)
                {
                    var triangle = new StreamGeometry();
                    using (var figure = triangle.Open())
                    {
                        figure.BeginFigure(area.TopRight, true, true);
                        figure.LineTo(new(area.Right - 7, area.Top), true, false);
                        figure.LineTo(new(area.Right, area.Top + 7), true, false);
                    }
                    drawing.DrawGeometry(error, null, triangle);
                }
            }

            // Cell borders go over the neighbouring grid lines.
            var borders = new ShapeBatch();
            foreach (var cell in paneCells)
            {
                if (cell.Style.Borders == CellBorders.None)
                    continue;
                var area = Area(cell.Bounds);
                var pen = cell.Style.BorderColor is { } border ? BorderPen(border) : inkPen;
                if (cell.Style.Borders.HasFlag(CellBorders.Left)) borders.Line(pen, area.TopLeft, area.BottomLeft);
                if (cell.Style.Borders.HasFlag(CellBorders.Top)) borders.Line(pen, area.TopLeft, area.TopRight);
                if (cell.Style.Borders.HasFlag(CellBorders.Right)) borders.Line(pen, area.TopRight, area.BottomRight);
                if (cell.Style.Borders.HasFlag(CellBorders.Bottom)) borders.Line(pen, area.BottomLeft, area.BottomRight);
            }
            borders.Stroke(drawing);

            foreach (var cell in paneCells)
                if (cell.Active)
                    drawing.DrawRectangle(null, activePen, Area(cell.Bounds));
            // The range a formula points at is outlined with a dashed line, as Excel marks it in Point mode.
            if (owner.PointRange is { } pointed &&
                SheetViewport.Span(viewport.Rows, pointed.Start.Row, pointed.End.Row, rowFrozen) is { } pointRows &&
                SheetViewport.Span(viewport.Columns, pointed.Start.Column, pointed.End.Column, columnFrozen) is { } pointColumns)
                drawing.DrawRectangle(null, pointPen, new Rect(pointColumns.Start + 1, pointRows.Start + 1,
                    Math.Max(0, pointColumns.End - pointColumns.Start - 2), Math.Max(0, pointRows.End - pointRows.Start - 2)));
            // A fill or move drag previews the range it will write with a dashed outline.
            if (dragPreview is { } preview &&
                SheetViewport.Span(viewport.Rows, preview.Start.Row, preview.End.Row, rowFrozen) is { } previewRows &&
                SheetViewport.Span(viewport.Columns, preview.Start.Column, preview.End.Column, columnFrozen) is { } previewColumns)
                drawing.DrawRectangle(null, dragPen, new Rect(previewColumns.Start + 1, previewRows.Start + 1,
                    Math.Max(0, previewColumns.End - previewColumns.Start - 2), Math.Max(0, previewRows.End - previewRows.Start - 2)));
            // The fill handle sits at the bottom-right of the selection, in the pane that shows that corner.
            if (owner.Mode == EditorMode.Full && viewport.CellBounds(selection.End) is { } corner &&
                SheetViewport.Span(viewport.Rows, selection.End.Row, selection.End.Row, rowFrozen) is not null &&
                SheetViewport.Span(viewport.Columns, selection.End.Column, selection.End.Column, columnFrozen) is not null)
                drawing.DrawRectangle(primary, handlePen, new Rect(corner.X + corner.Width - 4, corner.Y + corner.Height - 4, 8, 8));
            drawing.Pop();
        }

        long cellsDone = System.Diagnostics.Stopwatch.GetTimestamp();
        // Headers are painted last, over cells scrolled partly under them.
        drawing.DrawRectangle(header, null, new Rect(0, 0, ActualWidth, SheetViewport.ColumnHeaderHeight));
        drawing.DrawRectangle(header, null, new Rect(0, 0, SheetViewport.RowHeaderWidth, ActualHeight));
        var headerSelected = Brush("SecondaryContainer");
        foreach (var row in viewport.Rows)
        {
            var (start, end) = SheetViewport.Visible(row, SheetViewport.ColumnHeaderHeight, viewport.ScrolledTop);
            if (end <= start)
                continue;
            var area = new Rect(0, row.Start, SheetViewport.RowHeaderWidth, row.Length);
            drawing.PushClip(new RectangleGeometry(new Rect(0, start, SheetViewport.RowHeaderWidth, end - start)));
            if (row.Index >= selection.Start.Row && row.Index <= selection.End.Row)
                drawing.DrawRectangle(headerSelected, null, area);
            drawing.DrawRectangle(null, line, area);
            DrawText(drawing, (row.Index + 1).ToString(CultureInfo.InvariantCulture), area, ink, HeaderStyle, TextAlignment.Center);
            drawing.Pop();
        }
        foreach (var column in viewport.Columns)
        {
            var (start, end) = SheetViewport.Visible(column, SheetViewport.RowHeaderWidth, viewport.ScrolledLeft);
            if (end <= start)
                continue;
            var area = new Rect(column.Start, 0, column.Length, SheetViewport.ColumnHeaderHeight);
            drawing.PushClip(new RectangleGeometry(new Rect(start, 0, end - start, SheetViewport.ColumnHeaderHeight)));
            if (column.Index >= selection.Start.Column && column.Index <= selection.End.Column)
                drawing.DrawRectangle(headerSelected, null, area);
            drawing.DrawRectangle(null, line, area);
            DrawText(drawing, CellAddress.ColumnName(column.Index), area, ink, HeaderStyle, TextAlignment.Center);
            drawing.Pop();
        }
        drawing.DrawRectangle(header, line, new Rect(0, 0, SheetViewport.RowHeaderWidth, SheetViewport.ColumnHeaderHeight));
        if (session.ActiveSheet.FrozenRows > 0 && viewport.ScrolledTop > SheetViewport.ColumnHeaderHeight)
            drawing.DrawLine(new(Brush("Outline"), 2), new(0, viewport.ScrolledTop), new(ActualWidth, viewport.ScrolledTop));
        if (session.ActiveSheet.FrozenColumns > 0 && viewport.ScrolledLeft > SheetViewport.RowHeaderWidth)
            drawing.DrawLine(new(Brush("Outline"), 2), new(viewport.ScrolledLeft, 0), new(viewport.ScrolledLeft, ActualHeight));
        long done = System.Diagnostics.Stopwatch.GetTimestamp();
        RenderTimings = (Ms(started, built), Ms(built, bars), Ms(bars, cellsDone), Ms(cellsDone, done));
        static double Ms(long from, long to) => (to - from) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    /// <summary>Milliseconds the last frame spent building the viewport, updating scroll bars, painting cells and headers.</summary>
    internal (double Viewport, double ScrollBars, double Cells, double Headers) RenderTimings { get; private set; }

    private static readonly SheetStyle HeaderStyle = new();

    /// <summary>Collects rectangles per brush and lines per pen, and draws each set as one frozen geometry.</summary>
    private sealed class ShapeBatch
    {
        private readonly Dictionary<object, (StreamGeometry Geometry, StreamGeometryContext Context)> sets = new();

        // Rectangles of one brush that touch along a row are filled as one rectangle.
        private readonly Dictionary<(Brush Brush, double Top, double Height), List<(double From, double To)>> spans = new();

        public void Rectangle(Brush brush, Rect area)
        {
            if (area.Width <= 0 || area.Height <= 0)
                return;
            var key = (brush, area.Top, area.Height);
            if (!spans.TryGetValue(key, out var list))
                spans[key] = list = [];
            list.Add((area.Left, area.Right));
        }

        private void FlushSpans()
        {
            foreach (var ((brush, top, height), list) in spans)
            {
                list.Sort();
                var context = Context(brush);
                double start = list[0].From, end = list[0].To;
                for (int i = 1; i <= list.Count; i++)
                {
                    if (i < list.Count && list[i].From <= end + .01)
                    {
                        end = Math.Max(end, list[i].To);
                        continue;
                    }
                    context.BeginFigure(new Point(start, top), true, true);
                    context.LineTo(new Point(end, top), false, false);
                    context.LineTo(new Point(end, top + height), false, false);
                    context.LineTo(new Point(start, top + height), false, false);
                    if (i < list.Count)
                        (start, end) = list[i];
                }
            }
            spans.Clear();
        }

        // Horizontal and vertical segments are kept per pen and line, so the shared edges of neighbouring cells and
        // runs of touching segments are drawn as one segment.
        private readonly Dictionary<(Pen Pen, bool Horizontal, double At), List<(double From, double To)>> runs = new();

        public void Line(Pen pen, Point from, Point to)
        {
            if (from.Y == to.Y || from.X == to.X)
            {
                bool horizontal = from.Y == to.Y;
                var key = (pen, horizontal, horizontal ? from.Y : from.X);
                if (!runs.TryGetValue(key, out var list))
                    runs[key] = list = [];
                double a = horizontal ? from.X : from.Y, b = horizontal ? to.X : to.Y;
                list.Add((Math.Min(a, b), Math.Max(a, b)));
                return;
            }
            var context = Context(pen);
            context.BeginFigure(from, false, false);
            context.LineTo(to, true, false);
        }

        private void FlushRuns()
        {
            foreach (var ((pen, horizontal, at), list) in runs)
            {
                list.Sort();
                var context = Context(pen);
                double start = list[0].From, end = list[0].To;
                for (int i = 1; i <= list.Count; i++)
                {
                    if (i < list.Count && list[i].From <= end + .5)
                    {
                        end = Math.Max(end, list[i].To);
                        continue;
                    }
                    context.BeginFigure(horizontal ? new Point(start, at) : new Point(at, start), false, false);
                    context.LineTo(horizontal ? new Point(end, at) : new Point(at, end), true, false);
                    if (i < list.Count)
                        (start, end) = list[i];
                }
            }
            runs.Clear();
        }

        public void Fill(DrawingContext drawing)
        {
            FlushSpans();
            foreach (var (key, set) in Close())
                drawing.DrawGeometry((Brush)key, null, set);
        }

        public void Stroke(DrawingContext drawing)
        {
            FlushRuns();
            foreach (var (key, set) in Close())
                drawing.DrawGeometry(null, (Pen)key, set);
        }

        private IEnumerable<(object Key, StreamGeometry Geometry)> Close()
        {
            foreach (var (key, set) in sets)
            {
                set.Context.Close();
                set.Geometry.Freeze();
                yield return (key, set.Geometry);
            }
            sets.Clear();
        }

        private StreamGeometryContext Context(object key)
        {
            if (!sets.TryGetValue(key, out var set))
            {
                var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
                sets[key] = set = (geometry, geometry.Open());
            }
            return set.Context;
        }
    }
    private const int TextCacheLimit = 8192;
    private readonly Dictionary<TextKey, DrawingGroup> texts = new();
    private readonly Dictionary<uint, Brush> colorBrushes = new();
    private readonly Dictionary<uint, Pen> borderPens = new();

    private readonly record struct TextKey(string Text, string Family, bool Bold, bool Italic, bool Underline, bool Strike, bool Wrap,
        double Size, double Width, double Height, TextAlignment Alignment, CellVerticalAlignment Vertical, int Indent, Brush Ink);
    // Each editor may run on its own dispatcher thread, so the cache is concurrent.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Family, bool Bold, bool Italic), Typeface> Typefaces = new();

    private static Typeface TypefaceOf(SheetStyle style) => Typefaces.GetOrAdd((style.FontFamily, style.Bold, style.Italic),
        key => new Typeface(new FontFamily(key.Family), key.Italic ? FontStyles.Italic : FontStyles.Normal,
            key.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal));

    private static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }

    private static Brush ColorBrush(Dictionary<uint, Brush> cache, uint color)
    {
        if (!cache.TryGetValue(color, out var brush))
        {
            brush = ColorBrush(color);
            brush.Freeze();
            cache[color] = brush;
        }
        return brush;
    }

    private static Rect Area(SheetRectangle rectangle) =>
        new(rectangle.X, rectangle.Y, Math.Max(0, rectangle.Width), Math.Max(0, rectangle.Height));

    private bool CanDrawText(Rect area, SheetStyle style) =>
        area.Width >= 18 && area.Height >= 6 && style.FontSize * owner.Zoom >= SheetViewport.MinimumTextSize;

    private void DrawText(DrawingContext drawing, string text, Rect area, Brush ink, SheetStyle style, TextAlignment alignment)
    {
        if (text.Length == 0 || !CanDrawText(area, style))
            return;
        // Text is laid out at 100% and scaled to the zoom, so one layout serves every zoom level, including each frame of a
        // zoom glide. Laying text out is the costliest part of a frame; each layout is recorded once into a frozen drawing
        // at the cell's origin, clipped to the cell, and cells that stay in view reuse it.
        double zoom = owner.Zoom;
        double size = Math.Max(1, style.FontSize);
        double width = Math.Round(area.Width / zoom, 1), height = Math.Round(area.Height / zoom, 1);
        var key = new TextKey(text, style.FontFamily, style.Bold, style.Italic, style.Underline, style.Strike, style.Wrap,
            size, width, height, alignment, style.VerticalAlignment, style.Indent, ink);
        if (!texts.TryGetValue(key, out var cached))
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, TypefaceOf(style),
                size, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, width - 16), MaxTextHeight = Math.Max(1, height),
                MaxLineCount = style.Wrap ? int.MaxValue : 1, Trimming = TextTrimming.CharacterEllipsis, TextAlignment = alignment
            };
            if (style.Underline || style.Strike)
            {
                var decorations = new TextDecorationCollection();
                if (style.Underline) decorations.Add(TextDecorations.Underline[0]);
                if (style.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
                formatted.SetTextDecorations(decorations);
            }
            double y = style.VerticalAlignment switch
            {
                CellVerticalAlignment.Top => 0,
                CellVerticalAlignment.Bottom => height - formatted.Height,
                _ => (height - formatted.Height) / 2
            };
            var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, width, height)) };
            using (var context = group.Open())
                context.DrawText(formatted, new(8 + style.Indent * 4, y));
            // Theme brushes are usually frozen; a live brush keeps the drawing unfrozen but still reusable.
            if (group.CanFreeze)
                group.Freeze();
            texts[key] = cached = group;
        }
        var offset = new MatrixTransform(zoom, 0, 0, zoom, area.X, area.Y);
        offset.Freeze();
        drawing.PushTransform(offset);
        drawing.DrawDrawing(cached);
        drawing.Pop();
    }

    private Pen BorderPen(uint color)
    {
        if (!borderPens.TryGetValue(color, out var pen))
        {
            var brush = ColorBrush(color);
            brush.Freeze();
            borderPens[color] = pen = Frozen(new Pen(brush, 1));
        }
        return pen;
    }

    private enum DragKind { None, Fill, Move }

    private DragKind dragKind;
    private bool fillMenu;
    private CellAddress moveGrab;
    private CellAddress? moveTo;
    private CellRange? fillTarget;

    /// <summary>The range a fill or move drag will write, outlined while the drag runs.</summary>
    internal CellRange? DragPreview => dragKind switch
    {
        DragKind.Fill => fillTarget,
        DragKind.Move when moveTo is { } to => new CellRange(to,
            new CellAddress(to.Row + owner.Session.Selection.Height - 1, to.Column + owner.Session.Selection.Width - 1)),
        _ => null
    };

    /// <summary>The selection's outline in surface coordinates, across the panes that show it.</summary>
    private Rect? SelectionArea()
    {
        if (Viewport is not { } viewport)
            return null;
        var selection = owner.Session.Selection;
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        foreach (bool frozen in new[] { true, false })
        {
            if (SheetViewport.Span(viewport.Rows, selection.Start.Row, selection.End.Row, frozen) is { } rows)
                (top, bottom) = (Math.Min(top, rows.Start), Math.Max(bottom, rows.End));
            if (SheetViewport.Span(viewport.Columns, selection.Start.Column, selection.End.Column, frozen) is { } columns)
                (left, right) = (Math.Min(left, columns.Start), Math.Max(right, columns.End));
        }
        return right > left && bottom > top ? new Rect(left, top, right - left, bottom - top) : null;
    }

    /// <summary>Whether a point is on the fill handle at the selection's bottom-right corner.</summary>
    private bool OnFillHandle(Point point)
    {
        if (owner.Mode != EditorMode.Full || Viewport is not { } viewport || owner.CanPoint ||
            viewport.CellBounds(owner.Session.Selection.End) is not { } corner)
            return false;
        var handle = new Rect(corner.X + corner.Width - 6, corner.Y + corner.Height - 6, 12, 12);
        return handle.Contains(point) && InCells(point);
    }

    /// <summary>Whether a point is on the selection's outline, where a drag moves the selected cells.</summary>
    private bool OnSelectionBorder(Point point)
    {
        if (owner.Mode != EditorMode.Full || owner.CanPoint || SelectionArea() is not { } area || !InCells(point))
            return false;
        var outer = area;
        outer.Inflate(3, 3);
        var inner = area;
        if (inner.Width > 6 && inner.Height > 6)
            inner.Inflate(-3, -3);
        else
            inner = Rect.Empty;
        return outer.Contains(point) && (inner.IsEmpty || !inner.Contains(point));
    }

    private static bool InCells(Point point) =>
        point.X >= SheetViewport.RowHeaderWidth && point.Y >= SheetViewport.ColumnHeaderHeight;

    /// <summary>The cell under a point, clamped into the cell area so a drag past an edge keeps tracking the last cell.</summary>
    private CellAddress? ClampedHit(Point point)
    {
        double x = Math.Clamp(point.X, SheetViewport.RowHeaderWidth + 1, Math.Max(SheetViewport.RowHeaderWidth + 1, ActualWidth - 1));
        double y = Math.Clamp(point.Y, SheetViewport.ColumnHeaderHeight + 1, Math.Max(SheetViewport.ColumnHeaderHeight + 1, ActualHeight - 1));
        return Viewport?.HitTest(x, y);
    }

    /// <summary>The fill range for a pointer cell: the selection extended along the axis the pointer has moved furthest.</summary>
    private static CellRange FillTarget(CellRange source, CellAddress pointer)
    {
        int below = pointer.Row - source.End.Row, above = source.Start.Row - pointer.Row;
        int right = pointer.Column - source.End.Column, left = source.Start.Column - pointer.Column;
        int vertical = Math.Max(below, above), horizontal = Math.Max(right, left);
        if (vertical <= 0 && horizontal <= 0)
            return source;
        if (vertical >= horizontal)
            return below > 0 ? new(source.Start, new CellAddress(pointer.Row, source.End.Column))
                : new(new CellAddress(pointer.Row, source.Start.Column), source.End);
        return right > 0 ? new(source.Start, new CellAddress(source.End.Row, pointer.Column))
            : new(new CellAddress(source.Start.Row, pointer.Column), source.End);
    }

    private void BeginDrag(DragKind kind, CellAddress grab, bool menu = false)
    {
        dragKind = kind;
        fillMenu = menu;
        moveGrab = grab;
        moveTo = null;
        fillTarget = null;
        Cursor = kind == DragKind.Fill ? Cursors.Cross : Cursors.SizeAll;
        CaptureMouse();
    }

    private void CancelDrag()
    {
        bool active = dragKind != DragKind.None;
        dragKind = DragKind.None;
        fillTarget = null;
        moveTo = null;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        if (active)
            InvalidateVisual();
    }

    private void TrackDrag(Point point)
    {
        if (ClampedHit(point) is not { } cell)
            return;
        var selection = owner.Session.Selection;
        if (dragKind == DragKind.Fill)
        {
            var target = FillTarget(selection, cell);
            if (target != fillTarget)
            {
                fillTarget = target;
                owner.Reveal(cell);
                InvalidateVisual();
            }
            return;
        }
        int rows = Math.Clamp(cell.Row - moveGrab.Row, -selection.Start.Row, CellAddress.MaxRows - 1 - selection.End.Row);
        int columns = Math.Clamp(cell.Column - moveGrab.Column, -selection.Start.Column, CellAddress.MaxColumns - 1 - selection.End.Column);
        var to = new CellAddress(selection.Start.Row + rows, selection.Start.Column + columns);
        if (to != moveTo)
        {
            moveTo = to;
            owner.Reveal(cell);
            InvalidateVisual();
        }
    }

    private void FinishDrag(Point point)
    {
        var kind = dragKind;
        var target = fillTarget;
        var to = moveTo;
        bool menu = fillMenu;
        CancelDrag();
        var source = owner.Session.Selection;
        bool control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (kind == DragKind.Fill && target is { } range && range != source)
        {
            if (menu)
                owner.ShowFillMenu(source, range);
            else
                owner.FillFromHandle(source, range, control ? SheetFillMode.Alternate : SheetFillMode.Series);
        }
        else if (kind == DragKind.Move && to is { } destination && destination != source.Start)
            owner.MoveSelection(source, destination, control);
        UpdateHoverCursor(point);
    }

    private void UpdateHoverCursor(Point point) =>
        Cursor = OnFillHandle(point) ? Cursors.Cross : OnSelectionBorder(point) ? Cursors.SizeAll : null;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonDown(args);
        var point = args.GetPosition(this);
        if (args.ClickCount == 2 && owner.Mode == EditorMode.Full && point.Y < SheetViewport.ColumnHeaderHeight &&
            Viewport?.Columns.FirstOrDefault(column => Math.Abs(column.Start + column.Length - point.X) <= 3) is { Length: > 0 } header)
        {
            var column = new CellAddress(0, header.Index);
            owner.Session.AutoFitColumns(new(column, column), MeasureText);
            args.Handled = true;
            return;
        }
        if (Viewport?.HitTest(point.X, point.Y) is not { } address)
            return;
        // Point mode while editing a formula: the click inserts a reference and the editor keeps the keyboard.
        if (owner.CanPoint)
        {
            owner.PointAt(address, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            pointing = true;
            CaptureMouse();
            args.Handled = true;
            return;
        }
        // The fill handle and the selection outline start a smart drag: fill a series, or move the cells.
        bool onHandle = OnFillHandle(point), onBorder = !onHandle && OnSelectionBorder(point);
        owner.EndEdit(true);
        Focus();
        if (onHandle && args.ClickCount == 2)
            owner.FillDownToNeighbour(owner.Session.Selection);
        else if (onHandle)
            BeginDrag(DragKind.Fill, address);
        else if (onBorder)
            BeginDrag(DragKind.Move, address);
        else if (args.ClickCount == 2)
            owner.BeginEdit(address, null, point);
        else
        {
            anchor = Keyboard.Modifiers == ModifierKeys.Shift ? owner.Session.ActiveCell : address;
            owner.SelectAndReveal(anchor.Value, address);
            CaptureMouse();
        }
        args.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs args)
    {
        base.OnMouseRightButtonDown(args);
        var point = args.GetPosition(this);
        // A right-drag of the fill handle ends in a menu of fill choices.
        if (dragKind == DragKind.None && OnFillHandle(point) && Viewport?.HitTest(point.X, point.Y) is { } address)
        {
            owner.EndEdit(true);
            Focus();
            BeginDrag(DragKind.Fill, address, menu: true);
            args.Handled = true;
            return;
        }
        // A right-click outside the selection selects the cell under it, so the menu acts on what was clicked.
        if (dragKind == DragKind.None && !owner.CanPoint && Viewport?.HitTest(point.X, point.Y) is { } cell &&
            !owner.Session.Selection.Contains(cell))
        {
            owner.EndEdit(true);
            Focus();
            owner.SelectAndReveal(cell, cell);
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs args)
    {
        if (dragKind == DragKind.Fill && fillMenu)
        {
            FinishDrag(args.GetPosition(this));
            // The fill menu replaces the cell context menu for this release.
            args.Handled = true;
            return;
        }
        base.OnMouseRightButtonUp(args);
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (pointing && IsMouseCaptured)
        {
            var at = args.GetPosition(this);
            if (Viewport?.HitTest(at.X, at.Y) is { } target)
                owner.PointDrag(target);
            return;
        }
        var point = args.GetPosition(this);
        if (dragKind != DragKind.None)
        {
            if (IsMouseCaptured)
                TrackDrag(point);
            return;
        }
        if (!IsMouseCaptured)
        {
            UpdateHoverCursor(point);
            return;
        }
        if (anchor is null)
            return;
        if (Viewport?.HitTest(point.X, point.Y) is { } address && address != owner.Session.SelectionEnd)
            owner.SelectAndReveal(anchor.Value, address);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs args)
    {
        base.OnMouseLeftButtonUp(args);
        if (dragKind != DragKind.None && !fillMenu)
        {
            FinishDrag(args.GetPosition(this));
            args.Handled = true;
            return;
        }
        anchor = null;
        pointing = false;
        if (IsMouseCaptured && dragKind == DragKind.None)
            ReleaseMouseCapture();
    }

    protected override void OnLostMouseCapture(MouseEventArgs args)
    {
        base.OnLostMouseCapture(args);
        // Losing capture (another window, Alt+Tab) abandons a drag without writing anything.
        if (dragKind != DragKind.None)
        {
            dragKind = DragKind.None;
            fillTarget = null;
            moveTo = null;
            InvalidateVisual();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs args)
    {
        base.OnMouseWheel(args);
        var modifiers = Keyboard.Modifiers;
        owner.Wheel(args.Delta, modifiers.HasFlag(ModifierKeys.Control), modifiers.HasFlag(ModifierKeys.Shift),
            TimeSpan.FromMilliseconds((uint)args.Timestamp));
        args.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.Key == Key.Escape && dragKind != DragKind.None)
        {
            CancelDrag();
            args.Handled = true;
            return;
        }
        // Esc ends copy mode; Enter pastes the marked copy and ends it.
        if (args.Key == Key.Escape && owner.HasCopyMark)
        {
            owner.ClearCopyMark();
            args.Handled = true;
            return;
        }
        if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && owner.PasteCopyMarkOnEnter())
        {
            args.Handled = true;
            return;
        }
        var session = owner.Session;
        var sheet = session.ActiveSheet;
        var active = session.ActiveCell;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (args.Key == Key.F2)
        {
            args.Handled = owner.BeginEdit(active);
            return;
        }
        if (args.Key is Key.Tab or Key.Enter)
        {
            // Tab and Enter move the active cell (backwards with Shift); they never extend the selection.
            var moved = args.Key == Key.Tab ? SheetNavigation.Step(sheet, active, 0, shift ? -1 : 1)
                : SheetNavigation.Step(sheet, active, shift ? -1 : 1, 0);
            owner.SelectAndReveal(moved, moved);
            args.Handled = true;
            return;
        }
        (int Row, int Column)? direction = args.Key switch
        {
            Key.Left => (0, -1), Key.Right => (0, 1), Key.Up => (-1, 0), Key.Down => (1, 0), _ => null
        };
        if (direction is { } step)
        {
            // Shift extends the moving end and keeps the active cell, as in Excel.
            var next = SheetNavigation.Step(sheet, shift ? session.SelectionEnd : active, step.Row, step.Column);
            owner.SelectAndReveal(shift ? active : next, next);
            args.Handled = true;
            return;
        }
        if (args.Key == Key.Home)
        {
            var home = (shift ? session.SelectionEnd : active) with { Column = 0 };
            owner.SelectAndReveal(shift ? active : home, home);
            args.Handled = true;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs args)
    {
        base.OnTextInput(args);
        if (args.Text.Length > 0 && !char.IsControl(args.Text[0]))
            args.Handled = owner.BeginEdit(owner.Session.ActiveCell, args.Text);
    }

    /// <summary>Fits the widths of the selected columns to their contents.</summary>
    internal void AutoFit(CellRange range) =>
        owner.Session.AutoFitColumns(new(new(0, range.Start.Column), new(0, range.End.Column)), MeasureText);

    private double MeasureText(string text, SheetStyle style)
    {
        var typeface = TypefaceOf(style);
        return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface,
            Math.Clamp(style.FontSize, 1, 240), Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
    }

    private Brush Brush(string role) => TryFindResource("MatrixGrid.Brush." + role) as Brush ?? Brushes.Transparent;
    private static Brush ColorBrush(uint color) => new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
}
