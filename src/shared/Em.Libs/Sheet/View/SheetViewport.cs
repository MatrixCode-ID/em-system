// Ported from GridCraft (engine/src/display.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using Em.Sheet.Commands;
using Em.Sheet.Cells;
using Em.Sheet.Model;
using Em.Sheet.NumberFormats;

namespace Em.Sheet.View;

/// <summary>A rectangle in viewport coordinates, in device-independent pixels.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct SheetRectangle(double X, double Y, double Width, double Height)
{
    /// <summary>Checks whether a point lies inside; the right and bottom edges are exclusive.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>True when the point is inside.</returns>
    public bool Contains(double x, double y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

/// <summary>
/// One cell, or one merged area, in the viewport. <see cref="Bounds"/> may extend past the pane it is drawn in;
/// <see cref="Clip"/> is the visible part of that pane.
/// </summary>
public sealed record SheetDisplayCell(CellAddress Address, SheetRectangle Bounds, string Text,
    SheetStyle Style, bool Selected, bool Active, bool Error)
{
    /// <summary>Gets the visible part of <see cref="Bounds"/> within the pane the cell is drawn in.</summary>
    public SheetRectangle Clip { get; init; } = Bounds;
    /// <summary>Gets the merged range this display cell covers, or null for a single cell.</summary>
    public CellRange? Merge { get; init; }
}

/// <summary>The cells, row and column headers visible in a sheet view, computed for one scroll position and size.</summary>
public sealed class SheetViewport
{
    /// <summary>The width of the row header column.</summary>
    public const double RowHeaderWidth = 48;
    /// <summary>The height of the column header row.</summary>
    public const double ColumnHeaderHeight = 28;
    /// <summary>The width of a column without a stored width.</summary>
    public const double DefaultColumnWidth = 96;
    /// <summary>The height of a row without a stored height.</summary>
    public const double DefaultRowHeight = 28;

    /// <summary>The smallest zoomed font size, in device-independent pixels, at which cell text is formatted and drawn.</summary>
    public const double MinimumTextSize = 4;

    private readonly IReadOnlyList<CellRange> merges;

    /// <summary>
    /// Builds the visible cells. <paramref name="rowOffset"/> and <paramref name="columnOffset"/> are how far, in unzoomed
    /// sheet units, the first scrolled row and column are already scrolled out of view. Only cells that hold a value, a
    /// formula or a style, merged areas and the active cell become <see cref="Cells"/>; empty cells are drawn by the grid
    /// lines alone, so a far zoomed-out view costs no more than the cells it shows.
    /// </summary>
    public static SheetViewport Create(SheetSession session, int firstRow, int firstColumn, double width, double height,
        double zoom = 1, double rowOffset = 0, double columnOffset = 0)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(zoom, 0);
        var sheet = session.ActiveSheet;
        var rows = Axis(firstRow, rowOffset, sheet.FrozenRows, CellAddress.MaxRows, ColumnHeaderHeight, height,
            sheet.RowHeights, DefaultRowHeight, zoom, out double rowPane);
        var columns = Axis(firstColumn, columnOffset, sheet.FrozenColumns, CellAddress.MaxColumns, RowHeaderWidth, width,
            sheet.ColumnWidths, DefaultColumnWidth, zoom, out double columnPane);
        var viewport = new SheetViewport(rows, columns, rowPane, columnPane, width, height, sheet.Merges);
        var active = session.ActiveCell;
        var selection = session.Selection;
        var merges = new List<CellRange>();
        foreach (var merge in sheet.Merges)
            if (merge.Count > 1 && Overlaps(rows, merge.Start.Row, merge.End.Row) && Overlaps(columns, merge.Start.Column, merge.End.Column))
                merges.Add(merge);
        var cells = new List<SheetDisplayCell>();

        foreach (var row in rows)
        foreach (var column in columns)
        {
            var address = new CellAddress(row.Index, column.Index);
            bool stored = sheet.Cells.TryGetValue(address, out var cell);
            if (!stored && address != active || merges.Count > 0 && Covered(merges, address))
                continue;
            var bounds = new SheetRectangle(column.Start, row.Start, column.Length, row.Length);
            if (Display(session, sheet, address, cell, bounds, Clip(row.Frozen, column.Frozen),
                    selection.Contains(address), address == active, null, zoom) is { } shown)
                cells.Add(shown);
        }
        foreach (var merge in merges)
        {
            double left = Position(columns, merge.Start.Column, sheet.ColumnWidths, DefaultColumnWidth, zoom);
            double top = Position(rows, merge.Start.Row, sheet.RowHeights, DefaultRowHeight, zoom);
            double right = Position(columns, merge.End.Column, sheet.ColumnWidths, DefaultColumnWidth, zoom) +
                sheet.ColumnWidths.GetValueOrDefault(merge.End.Column, DefaultColumnWidth) * zoom;
            double bottom = Position(rows, merge.End.Row, sheet.RowHeights, DefaultRowHeight, zoom) +
                sheet.RowHeights.GetValueOrDefault(merge.End.Row, DefaultRowHeight) * zoom;
            var bounds = new SheetRectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
            // A merge is drawn in the pane of its top-left cell.
            bool rowFrozen = merge.Start.Row < sheet.FrozenRows, columnFrozen = merge.Start.Column < sheet.FrozenColumns;
            cells.Add(Display(session, sheet, merge.Start, sheet[merge.Start], bounds, Clip(rowFrozen, columnFrozen),
                selection.Intersects(merge), merge.Contains(active), merge, zoom, always: true)!);
        }
        viewport.Cells = cells;
        return viewport;

        SheetRectangle Clip(bool rowFrozen, bool columnFrozen)
        {
            double left = columnFrozen ? RowHeaderWidth : columnPane, top = rowFrozen ? ColumnHeaderHeight : rowPane;
            double right = columnFrozen ? columnPane : width, bottom = rowFrozen ? rowPane : height;
            return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
        }
    }

    private static SheetDisplayCell? Display(SheetSession session, SheetTab sheet, CellAddress address, SheetCell cell,
        SheetRectangle bounds, SheetRectangle clip, bool selected, bool active, CellRange? merge, double zoom, bool always = false)
    {
        var value = session.GetValue(sheet, address);
        var style = session.Workbook.Styles[cell.StyleIndex];
        // Text smaller than this many device-independent pixels is not drawn, so it is not formatted either.
        bool readable = style.FontSize * zoom >= MinimumTextSize;
        // Far zoomed out, a plain cell draws nothing beyond the grid lines; leaving it out keeps a frame free of
        // tens of thousands of short-lived objects.
        if (!always && !readable && !active && style.FillColor is null && style.Borders == CellBorders.None &&
            value.Kind != CellValueKind.Error)
            return null;
        string text = readable ? SheetNumberFormats.Format(value, style.NumberFormat, session.Workbook.DateSystem) : "";
        return new(address, bounds, text,
            style, selected, active, value.Kind == CellValueKind.Error) { Clip = clip, Merge = merge };
    }

    private static bool Overlaps(IReadOnlyList<SheetAxisCell> axis, int first, int last)
    {
        foreach (var item in axis)
            if (item.Index >= first && item.Index <= last)
                return true;
        return false;
    }

    private static bool Covered(List<CellRange> merges, CellAddress address)
    {
        foreach (var merge in merges)
            if (merge.Contains(address))
                return true;
        return false;
    }

    /// <summary>
    /// The visible extent, in one pane, of the items <paramref name="first"/> to <paramref name="last"/> of an axis,
    /// or <see langword="null"/> when none of them is visible in that pane.
    /// </summary>
    public static (double Start, double End)? Span(IReadOnlyList<SheetAxisCell> axis, int first, int last, bool frozen)
    {
        double start = double.PositiveInfinity, end = double.NegativeInfinity;
        foreach (var item in axis)
        {
            if (item.Frozen != frozen || item.Index < first || item.Index > last)
                continue;
            start = Math.Min(start, item.Start);
            end = Math.Max(end, item.Start + item.Length);
        }
        return start <= end ? (start, end) : null;
    }

    /// <summary>The bounds of a visible cell, or of the merged area that holds it; <see langword="null"/> when it is out of view.</summary>
    public SheetRectangle? CellBounds(CellAddress address)
    {
        foreach (var cell in Cells)
            if (cell.Merge is { } merge ? merge.Contains(address) : cell.Address == address)
                return cell.Bounds;
        SheetAxisCell? row = null, column = null;
        foreach (var item in Rows)
            if (item.Index == address.Row) { row = item; break; }
        foreach (var item in Columns)
            if (item.Index == address.Column) { column = item; break; }
        return row is { } r && column is { } c ? new SheetRectangle(c.Start, r.Start, c.Length, r.Length) : null;
    }

    private SheetViewport(IReadOnlyList<SheetAxisCell> rows, IReadOnlyList<SheetAxisCell> columns, double rowPane, double columnPane,
        double width, double height, IReadOnlyList<CellRange> merges)
    {
        (Rows, Columns, ScrolledTop, ScrolledLeft, Width, Height) = (rows, columns, rowPane, columnPane, width, height);
        this.merges = merges;
    }

    /// <summary>Gets the visible rows, frozen rows first.</summary>
    public IReadOnlyList<SheetAxisCell> Rows { get; }
    /// <summary>Gets the visible columns, frozen columns first.</summary>
    public IReadOnlyList<SheetAxisCell> Columns { get; }
    /// <summary>Gets the visible cells and merged areas to draw.</summary>
    public IReadOnlyList<SheetDisplayCell> Cells { get; private set; } = [];

    /// <summary>Where the scrolled (not frozen) rows begin, below the column header and any frozen rows.</summary>
    public double ScrolledTop { get; }

    /// <summary>Where the scrolled (not frozen) columns begin, right of the row header and any frozen columns.</summary>
    public double ScrolledLeft { get; }

    /// <summary>Gets the viewport width.</summary>
    public double Width { get; }
    /// <summary>Gets the viewport height.</summary>
    public double Height { get; }

    /// <summary>The cell under a point, or <see langword="null"/> over the headers. A point in a merged area returns its top-left cell.</summary>
    public CellAddress? HitTest(double x, double y)
    {
        int row = RowAt(y), column = ColumnAt(x);
        if (row < 0 || column < 0)
            return null;
        var address = new CellAddress(row, column);
        foreach (var merge in merges)
            if (merge.Contains(address))
                return merge.Start;
        return address;
    }

    /// <summary>The row at a vertical position below the column header, or -1.</summary>
    public int RowAt(double y) => IndexAt(Rows, y, ColumnHeaderHeight, ScrolledTop);

    /// <summary>The column at a horizontal position right of the row header, or -1.</summary>
    public int ColumnAt(double x) => IndexAt(Columns, x, RowHeaderWidth, ScrolledLeft);

    /// <summary>The visible extent of an axis item, clipped to its pane.</summary>
    public static (double Start, double End) Visible(SheetAxisCell item, double header, double scrolledStart) =>
        (Math.Max(item.Start, item.Frozen ? header : scrolledStart), item.Start + item.Length);

    /// <summary>Whether an axis item is fully inside its pane, within <paramref name="size"/>.</summary>
    public static bool IsFullyVisible(SheetAxisCell item, double header, double scrolledStart, double size) =>
        item.Start >= (item.Frozen ? header : scrolledStart) - 0.5 && item.Start + item.Length <= size + 0.5;

    private static int IndexAt(IReadOnlyList<SheetAxisCell> axis, double point, double header, double scrolledStart)
    {
        foreach (var segment in axis)
        {
            var (start, end) = Visible(segment, header, scrolledStart);
            if (point >= start && point < end)
                return segment.Index;
        }
        return -1;
    }

    private static double Position(IReadOnlyList<SheetAxisCell> axis, int index, IReadOnlyDictionary<int, double> sizes,
        double defaultSize, double zoom)
    {
        SheetAxisCell? reference = null;
        foreach (var item in axis)
        {
            if (item.Index == index)
                return item.Start;
            if (!item.Frozen && reference is null)
                reference = item;
        }
        if ((reference ?? (axis.Count > 0 ? axis[^1] : null)) is not { } anchor)
            return 0;
        double position = anchor.Start;
        if (index < anchor.Index)
            for (int i = index; i < anchor.Index; i++)
                position -= sizes.GetValueOrDefault(i, defaultSize) * zoom;
        else
            for (int i = anchor.Index; i < index; i++)
                position += sizes.GetValueOrDefault(i, defaultSize) * zoom;
        return position;
    }

    private static List<SheetAxisCell> Axis(int first, double offset, int frozen, int limit, double header, double size,
        IReadOnlyDictionary<int, double> sizes, double defaultSize, double zoom, out double scrolledStart)
    {
        var result = new List<SheetAxisCell>();
        double position = header;
        for (int i = 0; i < frozen && i < limit && position < size; i++)
        {
            double length = sizes.GetValueOrDefault(i, defaultSize) * zoom;
            result.Add(new(i, position, length, true));
            position += length;
        }
        scrolledStart = position;
        int start = Math.Clamp(first, frozen, limit - 1);
        position -= Math.Max(0, offset) * zoom;
        for (int i = start; i < limit && position < size; i++)
        {
            double length = sizes.GetValueOrDefault(i, defaultSize) * zoom;
            if (length > 0 && position + length > scrolledStart)
                result.Add(new(i, position, length, false));
            position += length;
        }
        return result;
    }
}

/// <summary>One visible row or column.</summary>
/// <param name="Index">The zero-based row or column index.</param>
/// <param name="Start">The position of its leading edge in the viewport.</param>
/// <param name="Length">Its height or width.</param>
/// <param name="Frozen">Whether it belongs to the frozen pane.</param>
public readonly record struct SheetAxisCell(int Index, double Start, double Length, bool Frozen = false);
