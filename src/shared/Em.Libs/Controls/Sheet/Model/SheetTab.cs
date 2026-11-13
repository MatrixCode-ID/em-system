// Ported from GridCraft (model/src/sheet.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Collections.ObjectModel;
using Em.Controls.Sheet.Cells;

namespace Em.Controls.Sheet.Model;

/// <summary>One worksheet of a <see cref="SheetWorkbook"/>: sparse cells, sizes, merges and frozen panes. Change it through a <see cref="Em.Controls.Sheet.Commands.SheetSession"/>.</summary>
public sealed class SheetTab
{
    private readonly Dictionary<CellAddress, SheetCell> cells = new();
    private readonly Dictionary<int, double> rowHeights = new();
    private readonly Dictionary<int, double> columnWidths = new();
    private readonly List<CellRange> merges = [];

    internal SheetTab(string name)
    {
        Name = name;
        Cells = new ReadOnlyDictionary<CellAddress, SheetCell>(cells);
        RowHeights = new ReadOnlyDictionary<int, double>(rowHeights);
        ColumnWidths = new ReadOnlyDictionary<int, double>(columnWidths);
        Merges = merges.AsReadOnly();
    }

    /// <summary>Gets the sheet name.</summary>
    public string Name { get; internal set; }
    /// <summary>Gets whether the sheet is hidden from the tab strip.</summary>
    public bool IsHidden { get; internal set; }
    /// <summary>Gets the number of frozen rows at the top.</summary>
    public int FrozenRows { get; internal set; }
    /// <summary>Gets the number of frozen columns at the left.</summary>
    public int FrozenColumns { get; internal set; }
    /// <summary>Gets the non-empty cells by address.</summary>
    public IReadOnlyDictionary<CellAddress, SheetCell> Cells { get; }
    /// <summary>Gets the row heights that differ from the default, by row index.</summary>
    public IReadOnlyDictionary<int, double> RowHeights { get; }
    /// <summary>Gets the column widths that differ from the default, by column index.</summary>
    public IReadOnlyDictionary<int, double> ColumnWidths { get; }
    /// <summary>Gets the merged ranges.</summary>
    public IReadOnlyList<CellRange> Merges { get; }
    /// <summary>Gets a cell, or an empty cell when nothing is stored at the address.</summary>
    /// <param name="address">The cell address.</param>
    public SheetCell this[CellAddress address] => cells.GetValueOrDefault(address);

    internal List<CellRange> MutableMerges => merges;
    internal Dictionary<int, double> MutableSizes(bool rows) => rows ? rowHeights : columnWidths;

    internal void Set(CellAddress address, SheetCell value)
    {
        if (value.IsEmpty)
            cells.Remove(address);
        else
            cells[address] = value;
    }
}
