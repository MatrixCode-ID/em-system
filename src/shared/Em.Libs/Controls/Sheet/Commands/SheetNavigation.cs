// Ported from GridCraft (engine/tests/it/merged_selection_keys.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
// Upstream sync: GridCraft crates/engine/tests/it/merged_selection_keys.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926.
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.Commands;

/// <summary>Keyboard movement between sheet cells.</summary>
public static class SheetNavigation
{
    /// <summary>Moves one cell in a direction, stepping over merged areas as one cell and staying inside the sheet.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="address">The current cell.</param>
    /// <param name="rowDirection">-1, 0 or 1 rows.</param>
    /// <param name="columnDirection">-1, 0 or 1 columns.</param>
    /// <returns>The new cell.</returns>
    public static CellAddress Step(SheetTab sheet, CellAddress address, int rowDirection, int columnDirection)
    {
        var merge = sheet.Merges.FirstOrDefault(range => range.Contains(address));
        int row = address.Row, column = address.Column;
        if (merge.Count > 1)
        {
            if (rowDirection != 0)
                row = rowDirection > 0 ? merge.End.Row : merge.Start.Row;
            if (columnDirection != 0)
                column = columnDirection > 0 ? merge.End.Column : merge.Start.Column;
        }
        var next = new CellAddress(Math.Clamp(row + Math.Sign(rowDirection), 0, CellAddress.MaxRows - 1),
            Math.Clamp(column + Math.Sign(columnDirection), 0, CellAddress.MaxColumns - 1));
        foreach (var range in sheet.Merges)
            if (range.Contains(next))
                return range.Start;
        return next;
    }
}
