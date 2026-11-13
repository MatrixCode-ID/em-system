using Em.Editors;

namespace Em.Grid;

/// <summary>Horizontal alignment of a column's header and cell content.</summary>
public enum GridColumnAlignment
{
    /// <summary>Numbers align right, booleans center, everything else left.</summary>
    Auto,
    /// <summary>Aligns cell content to the leading edge.</summary>
    Left,
    /// <summary>Centers cell content.</summary>
    Center,
    /// <summary>Aligns cell content to the trailing edge.</summary>
    Right
}

/// <summary>Describes one grid column: the row field it reads, its caption, value kind and presentation options.</summary>
/// <param name="Key">The field key used to read and update values in a row.</param>
/// <param name="Header">The caption shown in the column header.</param>
/// <param name="Kind">The value kind the column holds, which drives sorting, filtering, alignment and formatting.</param>
public sealed record GridColumn(string Key, string Header, GridValueKind Kind)
{
    /// <summary>Creates an empty text column, for XAML and serializers that need a parameterless constructor.</summary>
    public GridColumn() : this(string.Empty, string.Empty, GridValueKind.Text) { }

    /// <summary>Gets the requested content alignment; <c>Auto</c> lets <see cref="EffectiveAlignment"/> choose by value kind.</summary>
    public GridColumnAlignment Alignment { get; init; }

    /// <summary>The alignment in effect: <see cref="Alignment"/>, or for <see cref="GridColumnAlignment.Auto"/> right for numbers, center for booleans and left otherwise.</summary>
    public GridColumnAlignment EffectiveAlignment => Alignment != GridColumnAlignment.Auto ? Alignment
        : Kind switch
        {
            GridValueKind.Number => GridColumnAlignment.Right,
            GridValueKind.Boolean => GridColumnAlignment.Center,
            _ => GridColumnAlignment.Left
        };

    /// <summary>Gets the column width in device-independent pixels. The default is 120.</summary>
    public double Width { get; init; } = 120;
    /// <summary>Gets whether the column is shown. Hidden columns stay in the layout and can be shown again.</summary>
    public bool IsVisible { get; init; } = true;
    /// <summary>Gets whether the grid search and the finder look at this column's text. The default is true.</summary>
    public bool IsSearchable { get; init; } = true;
    /// <summary>Gets whether in-place editing is blocked for this column even when the grid is editable.</summary>
    public bool IsReadOnly { get; init; }
    /// <summary>Gets whether the column stays pinned at the leading edge while the grid scrolls horizontally.</summary>
    public bool IsFrozen { get; init; }
    /// <summary>Gets the optional band caption that groups this column with neighbouring columns under a shared header.</summary>
    public string? Band { get; init; }
    /// <summary>Gets the editor settings used to edit and format the column's values, or null for the default text editor.</summary>
    public EditorSettings? Editor { get; init; }
    /// <summary>Gets the display format taken from <see cref="Editor"/>, or null when no editor is set.</summary>
    public string? DisplayFormat => Editor?.DisplayFormat;
    /// <summary>Gets the number format written to spreadsheet exports, or null to derive one from the value kind.</summary>
    public string? SpreadsheetFormat { get; init; }

    /// <summary>Formats a value for display in this column, using the editor settings when present.</summary>
    /// <param name="value">The cell value to format.</param>
    /// <param name="culture">The culture used for numbers and dates.</param>
    /// <returns>The display text; booleans without an editor show a check mark for true and an empty string otherwise.</returns>
    public string Format(GridValue value, System.Globalization.CultureInfo culture)
    {
        if (Editor is { } editor)
        {
            var unwrapped = value.Kind switch
            {
                GridValueKind.Text => (object?)value.Text,
                GridValueKind.Number => value.Number,
                GridValueKind.Boolean => value.Boolean,
                GridValueKind.Date => value.Date,
                _ => null
            };
            return editor.Format(unwrapped);
        }
        return value.Kind == GridValueKind.Boolean ? value.Boolean == true ? "✓" : ""
            : value.Format(culture, Kind == GridValueKind.Date ? "dd MMM yyyy" : null);
    }
}
