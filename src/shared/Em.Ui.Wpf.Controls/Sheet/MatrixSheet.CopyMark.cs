// The copied-range marquee follows GridCraft (ui-egui/src/grid/selection.rs, selection_glide.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Model;
using Em.Controls.Sheet.View;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixSheet
{
    private (SheetTab Sheet, CellRange Range, bool Cut)? copyMark;
    private CopyMarkLayer? copyMarkLayer;
    private DispatcherTimer? copyMarkTimer;
    private bool keepCopyMark;

    /// <summary>The copied range, marked with marching ants on its sheet until Esc, Enter or an edit ends copy mode.</summary>
    internal CellRange? CopyMarkRange => copyMark is { } mark && mark.Sheet == Session.ActiveSheet ? mark.Range : null;

    internal bool HasCopyMark => copyMark is not null;

    /// <summary>Repaints the marquee after the sheet scrolled, zoomed or switched tabs.</summary>
    internal void InvalidateCopyMark()
    {
        if (copyMark is not null)
            copyMarkLayer?.InvalidateVisual();
    }

    private void SetCopyMark(CellRange range, bool cut)
    {
        copyMark = (Session.ActiveSheet, range, cut);
        copyMarkTimer ??= CreateCopyMarkTimer();
        copyMarkTimer.Start();
        copyMarkLayer?.InvalidateVisual();
    }

    internal void ClearCopyMark()
    {
        if (copyMark is null)
            return;
        copyMark = null;
        copyMarkTimer?.Stop();
        copyMarkLayer?.InvalidateVisual();
    }

    /// <summary>Runs a session change that keeps copy mode, such as pasting a copy; any other change ends it.</summary>
    private void KeepingCopyMark(Action change)
    {
        keepCopyMark = true;
        try
        {
            change();
        }
        finally
        {
            keepCopyMark = false;
        }
    }

    /// <summary>Enter while a copied range is marked pastes it into the selection and ends copy mode.</summary>
    internal bool PasteCopyMarkOnEnter()
    {
        if (copyMark is null || Mode != global::Em.Editing.EditorMode.Full)
            return false;
        ExecuteCommand("paste");
        ClearCopyMark();
        return true;
    }

    private DispatcherTimer CreateCopyMarkTimer()
    {
        // As in Excel, the dashes crawl one DIP every 50 ms, which reads as a flickering outline. Only the light overlay
        // repaints, not the cells.
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            if (copyMarkLayer is null || !IsVisible)
                return;
            copyMarkLayer.Phase = (copyMarkLayer.Phase + 1) % 8;
            copyMarkLayer.InvalidateVisual();
        };
        return timer;
    }

    /// <summary>A transparent layer over the cells that draws the marching ants, so their animation never repaints the sheet.</summary>
    internal sealed class CopyMarkLayer : FrameworkElement
    {
        private readonly MatrixSheet owner;
        private readonly SheetSurface surface;

        internal CopyMarkLayer(MatrixSheet owner, SheetSurface surface)
        {
            this.owner = owner;
            this.surface = surface;
            IsHitTestVisible = false;
            ClipToBounds = true;
        }

        internal int Phase { get; set; }

        protected override void OnRender(DrawingContext drawing)
        {
            if (owner.CopyMarkRange is not { } range || surface.Viewport is not { } viewport)
                return;
            var brush = TryFindResource("MatrixGrid.Brush.Primary") as Brush ?? Brushes.DodgerBlue;
            var contrast = TryFindResource("MatrixGrid.Brush.SurfaceContainerLowest") as Brush ?? Brushes.White;
            // An Excel-style two-tone marquee: 4-DIP primary dashes over a contrasting line, so the gaps stay visible on any
            // fill. Dash lengths are in pen widths; each step moves the dashes one DIP.
            var under = new Pen(contrast, 2);
            var pen = new Pen(brush, 2) { DashStyle = new DashStyle([2, 2], -Phase / 2.0) };
            under.Freeze();
            pen.Freeze();
            foreach (bool rowFrozen in new[] { true, false })
            foreach (bool columnFrozen in new[] { true, false })
            {
                double left = columnFrozen ? SheetViewport.RowHeaderWidth : viewport.ScrolledLeft;
                double right = columnFrozen ? viewport.ScrolledLeft : ActualWidth;
                double top = rowFrozen ? SheetViewport.ColumnHeaderHeight : viewport.ScrolledTop;
                double bottom = rowFrozen ? viewport.ScrolledTop : ActualHeight;
                if (right <= left || bottom <= top ||
                    SheetViewport.Span(viewport.Rows, range.Start.Row, range.End.Row, rowFrozen) is not { } rows ||
                    SheetViewport.Span(viewport.Columns, range.Start.Column, range.End.Column, columnFrozen) is not { } columns)
                    continue;
                drawing.PushClip(new RectangleGeometry(new Rect(left, top, right - left, bottom - top)));
                var outline = new Rect(columns.Start + 1, rows.Start + 1,
                    Math.Max(0, columns.End - columns.Start - 2), Math.Max(0, rows.End - rows.Start - 2));
                drawing.DrawRectangle(null, under, outline);
                drawing.DrawRectangle(null, pen, outline);
                drawing.Pop();
            }
        }
    }
}
