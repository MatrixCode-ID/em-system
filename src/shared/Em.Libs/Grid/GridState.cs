using System.Collections.Immutable;
using Em.Grid.Query;

namespace Em.Grid;

/// <summary>Identifies one cell by row key and column key.</summary>
/// <param name="Row">The row key.</param>
/// <param name="ColumnKey">The column key.</param>
public readonly record struct CellAddress(RowKey Row, string ColumnKey);

/// <summary>Immutable selection and query state of a grid view.</summary>
public sealed record GridState
{
    /// <summary>Gets the active query.</summary>
    public GridQuery Query { get; init; } = GridQuery.Empty;
    /// <summary>Gets the keys of the selected rows.</summary>
    public ImmutableHashSet<RowKey> SelectedRows { get; init; } = [];
    /// <summary>Gets the focused cell, or null when no cell has focus.</summary>
    public CellAddress? FocusedCell { get; init; }

    /// <summary>Focuses a cell and updates the row selection.</summary>
    /// <param name="address">The cell to focus.</param>
    /// <param name="extend">When true, toggles the row in the current selection; otherwise the row becomes the only selected row.</param>
    /// <returns>The new state.</returns>
    public GridState Select(CellAddress address, bool extend)
    {
        var selection = !extend ? ImmutableHashSet.Create(address.Row)
            : SelectedRows.Contains(address.Row) ? SelectedRows.Remove(address.Row)
            : SelectedRows.Add(address.Row);
        return this with { FocusedCell = address, SelectedRows = selection };
    }
}
