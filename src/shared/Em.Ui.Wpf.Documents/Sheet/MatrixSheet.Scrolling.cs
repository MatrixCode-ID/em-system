// Wheel and zoom behavior follows GridCraft (crates/ui-egui/src/grid.rs, grid/show.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Em.Sheet.Cells;
using Em.Sheet.View;

namespace Em.Ui.Wpf.Documents.Sheet;

public sealed partial class MatrixSheet
{
    /// <summary>Zoom percentages one Ctrl+wheel notch steps between: four per doubling, so 25, 50, 100, 200 and 400 are on it.</summary>
    internal static readonly int[] WheelZoomLadder = [10, 12, 15, 18, 21, 25, 30, 35, 42, 50, 60, 70, 85, 100, 120, 140, 170, 200, 240, 280, 340, 400];
    internal const double MinZoom = .1, MaxZoom = 4;

    // Sheet units one wheel notch scrolls: three default rows.
    private const double NotchDistance = 3 * SheetViewport.DefaultRowHeight;
    // Seconds without Ctrl+wheel input after which a partly turned notch is forgotten.
    private const double WheelZoomIdle = .75;

    private ScrollBar? verticalBar, horizontalBar;
    private double pendingRows, pendingColumns;
    private double zoomNotches;
    private TimeSpan lastZoomInput;
    private double? zoomTarget;
    private TimeSpan? lastFrame;
    private bool animating;
    private (global::Em.Sheet.Model.SheetTab Sheet, int Row, int Column)? usedExtent;

    /// <summary>How far, in unzoomed sheet units, <see cref="FirstRow"/> is already scrolled out of view.</summary>
    public double RowOffset { get; private set; }

    /// <summary>How far, in unzoomed sheet units, <see cref="FirstColumn"/> is already scrolled out of view.</summary>
    public double ColumnOffset { get; private set; }

    /// <summary>Sets the zoom (0.1 to 4) at once, ending any wheel zoom glide.</summary>
    public void SetZoom(double value)
    {
        zoomTarget = null;
        ApplyZoom(value);
        RefreshToolbar();
    }

    /// <summary>Scrolls by a distance in screen units, keeping row and column offsets within the sheet.</summary>
    public void ScrollBy(double horizontal, double vertical)
    {
        if (!double.IsFinite(horizontal) || !double.IsFinite(vertical))
            return;
        if (vertical != 0)
            Advance(true, vertical / zoom);
        if (horizontal != 0)
            Advance(false, horizontal / zoom);
        ScrollChangedVisual();
    }

    internal void Wheel(int delta, bool control, bool shift, TimeSpan time)
    {
        if (control)
        {
            WheelZoom(delta / 120.0, time);
            return;
        }
        double distance = -delta / 120.0 * NotchDistance;
        bool precise = delta % 120 != 0;
        if (precise)
        {
            if (shift) Advance(false, distance); else Advance(true, distance);
            ScrollChangedVisual();
            return;
        }
        if (shift) pendingColumns += distance; else pendingRows += distance;
        StartAnimation();
    }

    private void WheelZoom(double turned, TimeSpan time)
    {
        if ((time - lastZoomInput).TotalSeconds > WheelZoomIdle)
            zoomNotches = 0;
        lastZoomInput = time;
        if (double.IsFinite(turned))
            zoomNotches = Math.Clamp(zoomNotches + turned, -32, 32);
        // A notch steps once 60% of it has turned; steps count from where a glide is going.
        double from = zoomTarget ?? zoom, target = from;
        while (zoomNotches >= .6) { target = WheelZoomStep(target, true); zoomNotches -= 1; }
        while (zoomNotches <= -.6) { target = WheelZoomStep(target, false); zoomNotches += 1; }
        if (target == from)
            return;
        zoomTarget = target;
        StartAnimation();
    }

    /// <summary>
    /// The zoom one Ctrl+wheel notch goes to: the next ladder step in that direction, skipping a step less than 5% away,
    /// but never 100% or the 10%/400% limits.
    /// </summary>
    internal static double WheelZoomStep(double zoom, bool zoomIn)
    {
        int current = (int)Math.Round(Math.Clamp(zoom, MinZoom, MaxZoom) * 100);
        var ahead = WheelZoomLadder.Where(step => zoomIn ? step > current : step < current).ToList();
        if (!zoomIn)
            ahead.Reverse();
        if (ahead.Count == 0)
            return current / 100.0;
        int found = ahead.FirstOrDefault(step => step == 100 || (zoomIn ? step * 100 >= current * 105 : step * 105 <= current * 100), ahead[^1]);
        return found / 100.0;
    }

    private void StartAnimation()
    {
        if (animating)
            return;
        animating = true;
        lastFrame = null;
        CompositionTarget.Rendering += OnFrame;
    }

    private void StopAnimation()
    {
        if (!animating)
            return;
        animating = false;
        CompositionTarget.Rendering -= OnFrame;
    }

    private void OnFrame(object? sender, EventArgs args)
    {
        var now = (args as RenderingEventArgs)?.RenderingTime ?? TimeSpan.Zero;
        double dt = lastFrame is { } previous ? Math.Clamp((now - previous).TotalSeconds, 0, .1) : 1 / 60.0;
        if (lastFrame == now)
            return;
        lastFrame = now;
        if (!IsVisible)
        {
            FinishAnimation();
            return;
        }
        // About 90% of the remaining distance in 0.1 s, frame-rate independent.
        double share = 1 - Math.Pow(.1, dt / .1);
        bool moved = false;
        if (pendingRows != 0 || pendingColumns != 0)
        {
            double rows = Math.Abs(pendingRows) < .5 ? pendingRows : pendingRows * share;
            double columns = Math.Abs(pendingColumns) < .5 ? pendingColumns : pendingColumns * share;
            pendingRows -= rows;
            pendingColumns -= columns;
            if (rows != 0) Advance(true, rows);
            if (columns != 0) Advance(false, columns);
            moved = true;
        }
        if (zoomTarget is { } target)
        {
            double next = zoom * Math.Pow(target / zoom, share);
            if (Math.Abs(next - target) < .005)
            {
                zoomTarget = null;
                ApplyZoom(target);
                RefreshToolbar();
            }
            else
                ApplyZoom(next);
            moved = true;
        }
        if (moved)
            ScrollChangedVisual();
        if (pendingRows == 0 && pendingColumns == 0 && zoomTarget is null)
            StopAnimation();
    }

    private void FinishAnimation()
    {
        if (pendingRows != 0) Advance(true, pendingRows);
        if (pendingColumns != 0) Advance(false, pendingColumns);
        pendingRows = pendingColumns = 0;
        if (zoomTarget is { } target)
        {
            zoomTarget = null;
            ApplyZoom(target);
        }
        StopAnimation();
    }

    private void ApplyZoom(double value)
    {
        if (!double.IsFinite(value))
            return;
        zoom = Math.Clamp(value, MinZoom, MaxZoom);
        if (status is not null)
            status.Text = StatusText();
        ScrollChangedVisual();
    }

    // The mode, as Excel's status bar shows it: Ready, Enter (typing; an arrow moves on), Edit (F2; arrows move the caret)
    // or Point (an arrow or click inserts a reference).
    private string StatusText() => (cellEditor is not { } editor ? "Ready" : editor.EnterMode && editor.CanPoint ? "Point" :
        editor.EnterMode ? "Enter" : "Edit") + $"    {zoom:P0}";

    private void Advance(bool rows, double delta)
    {
        var sheet = Session.ActiveSheet;
        int frozen = rows ? sheet.FrozenRows : sheet.FrozenColumns;
        int limit = rows ? CellAddress.MaxRows : CellAddress.MaxColumns;
        int index = Math.Max(rows ? FirstRow : FirstColumn, frozen);
        double offset = (rows ? RowOffset : ColumnOffset) + delta;
        while (offset < 0 && index > frozen)
            offset += Size(rows, --index);
        offset = Math.Max(0, offset);
        // A small tolerance keeps eased steps that add up to a whole row from stopping a hair short of it.
        while (index < limit - 1 && offset >= Size(rows, index) - 1e-6)
            offset = Math.Max(0, offset - Size(rows, index++));
        if (offset < 1e-6)
            offset = 0;
        if (rows)
            (FirstRow, RowOffset) = (index, offset);
        else
            (FirstColumn, ColumnOffset) = (index, offset);
    }

    private double Size(bool rows, int index)
    {
        var sheet = Session.ActiveSheet;
        return rows ? sheet.RowHeights.GetValueOrDefault(index, SheetViewport.DefaultRowHeight)
            : sheet.ColumnWidths.GetValueOrDefault(index, SheetViewport.DefaultColumnWidth);
    }

    private void ScrollChangedVisual()
    {
        surface?.InvalidateVisual();
        PositionCellEditor();
    }

    /// <summary>Scrolls the least distance that shows a cell whole, if it is not already.</summary>
    internal void Reveal(CellAddress cell)
    {
        if (surface is null || surface.ActualWidth <= 0 || surface.ActualHeight <= 0)
        {
            ScrollTo(cell.Row, cell.Column);
            return;
        }
        var sheet = Session.ActiveSheet;
        double viewHeight = surface.ActualHeight - SheetViewport.ColumnHeaderHeight - FrozenLength(true);
        double viewWidth = surface.ActualWidth - SheetViewport.RowHeaderWidth - FrozenLength(false);
        if (cell.Row >= sheet.FrozenRows)
            RevealAxis(true, cell.Row, viewHeight / zoom);
        if (cell.Column >= sheet.FrozenColumns)
            RevealAxis(false, cell.Column, viewWidth / zoom);
        ScrollChangedVisual();
    }

    private double FrozenLength(bool rows)
    {
        int frozen = rows ? Session.ActiveSheet.FrozenRows : Session.ActiveSheet.FrozenColumns;
        double length = 0;
        for (int i = 0; i < frozen; i++)
            length += Size(rows, i) * zoom;
        return length;
    }

    private void RevealAxis(bool rows, int target, double view)
    {
        int first = Math.Max(rows ? FirstRow : FirstColumn, rows ? Session.ActiveSheet.FrozenRows : Session.ActiveSheet.FrozenColumns);
        double offset = rows ? RowOffset : ColumnOffset;
        if (target < first || target == first && offset > 0)
        {
            Set(target, 0);
            return;
        }
        // Distance from the visible top to the far edge of the target.
        double end = -offset;
        for (int i = first; i <= target && end <= view + 1; i++)
            end += Size(rows, i);
        if (end <= view + .5)
            return;
        if (target - first > 4096)
        {
            Set(target, 0);
            return;
        }
        Advance(rows, end - view);

        void Set(int index, double value)
        {
            if (rows) (FirstRow, RowOffset) = (index, value);
            else (FirstColumn, ColumnOffset) = (index, value);
        }
    }

    private void BuildScrollBars(System.Windows.Controls.Grid layers)
    {
        verticalBar = new ScrollBar { Orientation = System.Windows.Controls.Orientation.Vertical, SmallChange = 1 };
        horizontalBar = new ScrollBar { Orientation = System.Windows.Controls.Orientation.Horizontal, SmallChange = 1 };
        System.Windows.Controls.Grid.SetColumn(verticalBar, 1);
        System.Windows.Controls.Grid.SetRow(horizontalBar, 1);
        layers.Children.Add(verticalBar);
        layers.Children.Add(horizontalBar);
        verticalBar.Scroll += (_, args) => ScrollBarMoved(true, args);
        horizontalBar.Scroll += (_, args) => ScrollBarMoved(false, args);
    }

    private void ScrollBarMoved(bool rows, ScrollEventArgs args)
    {
        switch (args.ScrollEventType)
        {
            case ScrollEventType.SmallIncrement: Advance(rows, Size(rows, rows ? FirstRow : FirstColumn)); break;
            case ScrollEventType.SmallDecrement: Advance(rows, -Size(rows, Math.Max(0, (rows ? FirstRow : FirstColumn) - 1))); break;
            case ScrollEventType.LargeIncrement or ScrollEventType.LargeDecrement:
                var bar = rows ? verticalBar! : horizontalBar!;
                double page = Math.Max(1, bar.ViewportSize - 1) * (rows ? SheetViewport.DefaultRowHeight : SheetViewport.DefaultColumnWidth);
                Advance(rows, args.ScrollEventType == ScrollEventType.LargeIncrement ? page : -page);
                break;
            default:
                int frozen = rows ? Session.ActiveSheet.FrozenRows : Session.ActiveSheet.FrozenColumns;
                double value = Math.Max(0, args.NewValue);
                int index = Math.Clamp((int)Math.Floor(value) + frozen, frozen, (rows ? CellAddress.MaxRows : CellAddress.MaxColumns) - 1);
                double offset = (value - Math.Floor(value)) * Size(rows, index);
                if (rows) (FirstRow, RowOffset) = (index, offset);
                else (FirstColumn, ColumnOffset) = (index, offset);
                break;
        }
        ScrollChangedVisual();
    }

    /// <summary>Updates the scroll bars from the viewport just laid out; called by the surface after each render.</summary>
    internal void UpdateScrollBars(SheetViewport viewport)
    {
        if (verticalBar is null || horizontalBar is null)
            return;
        var sheet = Session.ActiveSheet;
        if (usedExtent?.Sheet != sheet)
        {
            int row = 0, column = 0;
            foreach (var address in sheet.Cells.Keys)
            {
                row = Math.Max(row, address.Row);
                column = Math.Max(column, address.Column);
            }
            usedExtent = (sheet, row, column);
        }
        var (_, lastRow, lastColumn) = usedExtent.Value;
        Set(verticalBar, true, FirstRow, RowOffset, sheet.FrozenRows, lastRow, viewport.Rows, CellAddress.MaxRows);
        Set(horizontalBar, false, FirstColumn, ColumnOffset, sheet.FrozenColumns, lastColumn, viewport.Columns, CellAddress.MaxColumns);

        void Set(ScrollBar bar, bool rows, int first, double offset, int frozen, int last, IReadOnlyList<SheetAxisCell> items, int limit)
        {
            int start = Math.Max(first, frozen);
            double value = start - frozen + offset / Math.Max(1, Size(rows, start));
            double visible = Math.Max(1, items.Count(item => !item.Frozen) - 1);
            // The range reaches past the used range and the current position, as in Excel.
            double maximum = Math.Min(limit - frozen - 1, Math.Max(last - frozen + 1, value + visible));
            bar.Maximum = Math.Max(0, maximum);
            bar.ViewportSize = visible;
            bar.LargeChange = visible;
            bar.Value = Math.Min(value, bar.Maximum);
        }
    }
}
