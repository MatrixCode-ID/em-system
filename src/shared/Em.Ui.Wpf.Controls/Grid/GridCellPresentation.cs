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
public sealed record GridCellInfo(GridRow Row, GridColumn Column, GridValue Value, string Text, bool IsSelected);

/// <summary>Customizes how the cells of one column are drawn.</summary>
public sealed class GridCellPresentation
{
    /// <summary>Gets or sets the key of the column to customize.</summary>
    public string ColumnKey { get; set; } = "";
    /// <summary>Gets or sets a template that replaces the default cell text; its data context is a <see cref="GridCellInfo"/>.</summary>
    public DataTemplate? Template { get; set; }
    /// <summary>Gets or sets a function that returns a cell background, or null to keep the default.</summary>
    public Func<GridCellInfo, Brush?>? Background { get; set; }
}
