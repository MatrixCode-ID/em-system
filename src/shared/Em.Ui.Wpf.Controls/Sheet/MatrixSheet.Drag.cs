// Fill-handle and drag-and-drop behaviour follows GridCraft (ui-egui/src/grid/interact.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Windows;
using System.Windows.Controls;
using Em.Controls.Sheet.Commands;
using Em.Controls.Sheet.Cells;
using Em.Ui.Wpf.Controls;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixSheet
{
    /// <summary>
    /// Fills from the selection into a range dragged out with the fill handle, then selects the filled range with the active
    /// cell kept.
    /// </summary>
    internal void FillFromHandle(CellRange source, CellRange target, SheetFillMode mode)
    {
        var result = Session.Fill(source, target, mode);
        if (!result.Accepted)
        {
            ShowStatus(result.Reason);
            return;
        }
        var active = Session.ActiveCell;
        var anchor = target.Contains(active) ? active : target.Start;
        var end = new CellAddress(anchor.Row == target.Start.Row ? target.End.Row : target.Start.Row,
            anchor.Column == target.Start.Column ? target.End.Column : target.Start.Column);
        SelectAndReveal(anchor, end);
    }

    /// <summary>A double-click on the fill handle fills down as far as the neighbouring column holds data.</summary>
    internal void FillDownToNeighbour(CellRange source)
    {
        var sheet = Session.ActiveSheet;
        int neighbour = source.Start.Column > 0 ? source.Start.Column - 1 : source.End.Column + 1;
        if (neighbour >= CellAddress.MaxColumns)
            return;
        int cap = (int)Math.Min(CellAddress.MaxRows - 1L, source.End.Row + 1_000_000L);
        int last = source.End.Row;
        while (last < cap && sheet[new CellAddress(last + 1, neighbour)] is var cell &&
               (cell.Formula is not null || cell.Value.Kind != CellValueKind.Blank))
            last++;
        if (last > source.End.Row)
            FillFromHandle(source, new(source.Start, new CellAddress(last, source.End.Column)), SheetFillMode.Series);
    }

    /// <summary>The menu a right-drag of the fill handle ends in: how to fill the dragged range.</summary>
    internal void ShowFillMenu(CellRange source, CellRange target) => OpenFillMenu(source, target);

    /// <summary>
    /// Drops the selection dragged by its outline at a new top-left cell: moves it, or copies it with Ctrl. Replacing cells
    /// that hold data asks first.
    /// </summary>
    internal void MoveSelection(CellRange source, CellAddress to, bool copy)
    {
        if (Session.MoveWouldReplace(source, to, copy) && !ConfirmReplace())
            return;
        var result = Session.Move(source, to, copy);
        if (!result.Accepted)
        {
            ShowStatus(result.Reason);
            return;
        }
        var active = Session.ActiveCell;
        int rows = to.Row - source.Start.Row, columns = to.Column - source.Start.Column;
        var anchor = new CellAddress(active.Row + rows, active.Column + columns);
        var moved = new CellRange(to, new CellAddress(to.Row + source.Height - 1, to.Column + source.Width - 1));
        var end = new CellAddress(anchor.Row == moved.Start.Row ? moved.End.Row : moved.Start.Row,
            anchor.Column == moved.Start.Column ? moved.End.Column : moved.Start.Column);
        SelectAndReveal(moved.Contains(anchor) ? anchor : moved.Start, moved.Contains(anchor) ? end : moved.End);
    }

    private bool ConfirmReplace()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "There's already data here. Do you want to replace it?", TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = Material.Button("Cancel", "TextButton");
        var ok = Material.Button("OK", "TextButton");
        cancel.IsCancel = true;
        ok.IsDefault = true;
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        stack.Children.Add(actions);
        var window = Material.Dialog(this, "MatrixSheet", stack, 340);
        ok.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true;
    }

    private void ShowStatus(string? message)
    {
        if (status is not null && !string.IsNullOrEmpty(message))
            status.Text = message;
    }
}
