using System.Windows;
using System.Windows.Media;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;

namespace Em.Ui.Wpf.Controls;

/// <summary>The data passed to cell templates and background selectors.</summary>
/// <param name="Row">The row.</param>
/// <param name="Column">The column.</param>
/// <param name="Value">The cell value.</param>
/// <param name="Text">The formatted cell text.</param>
/// <param name="IsSelected">Whether the row is selected.</param>
public sealed record GridCellInfo(GridRow Row, GridColumn Column, GridValue Value, string Text, bool IsSelected)
{
    /// <summary>
    /// Gets the object of the row: the source object when the grid is bound to <see cref="MatrixGrid.ItemsSource"/>,
    /// or the <see cref="GridRow"/> for a provider.
    /// </summary>
    public object? Item { get; init; }
}

/// <summary>Customizes how the cells of one column are drawn.</summary>
public sealed class GridCellPresentation
{
    /// <summary>Gets or sets the key of the column to customize.</summary>
    public string ColumnKey { get; set; } = "";
    /// <summary>Gets or sets a template that replaces the default cell text; its data context is a <see cref="GridCellInfo"/>.</summary>
    public DataTemplate? Template { get; set; }
    /// <summary>Gets or sets a function that returns a cell background, or null to keep the default. Selected rows keep the selection colour.</summary>
    public Func<GridCellInfo, Brush?>? Background { get; set; }
    /// <summary>Gets or sets a function that returns the cell text colour, or null to keep the default. Selected rows keep the selection colour.</summary>
    public Func<GridCellInfo, Brush?>? Foreground { get; set; }
    /// <summary>Gets or sets a function that returns the cell opacity from 0 to 1, applied to the cell background and text.</summary>
    public Func<GridCellInfo, double>? Opacity { get; set; }
    /// <summary>Gets or sets a function that returns a tooltip shown while the pointer rests on the cell, or null for none.</summary>
    public Func<GridCellInfo, object?>? ToolTip { get; set; }
    /// <summary>
    /// Gets or sets a function that replaces the displayed text of a cell, or returns null to keep the formatted value.
    /// Only the shown text changes: sorting, filtering and summaries keep the cell value. Copy, best fit, finder
    /// highlighting and exports use the replaced text. <see cref="GridCellInfo.Text"/> holds the formatted value.
    /// Exports may call the function on worker threads.
    /// </summary>
    public Func<GridCellInfo, string?>? DisplayText { get; set; }
}
