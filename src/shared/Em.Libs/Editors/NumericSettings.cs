using System.Globalization;

namespace Em.Editors;

/// <summary>Settings for numeric and spin editors.</summary>
public sealed record NumericSettings : EditorSettings
{
    /// <summary>Gets the smallest accepted value, or null for no limit.</summary>
    public decimal? Minimum { get; init; }
    /// <summary>Gets the largest accepted value, or null for no limit.</summary>
    public decimal? Maximum { get; init; }
    /// <summary>Gets the step used by spin buttons and arrow keys. The default is 1.</summary>
    public decimal Increment { get; init; } = 1;
    /// <summary>Gets the format used while editing, or null for the plain number.</summary>
    public string? EditFormat { get; init; }
    /// <summary>Gets whether numbers with a fractional part are accepted. The default is true; false accepts whole numbers only.</summary>
    public bool AllowDecimals { get; init; } = true;

    /// <inheritdoc/>
    protected override EditorResult ParseValue(string text)
    {
        if (!decimal.TryParse(text, NumberStyles.Number, Culture, out decimal number))
            return EditorResult.Reject("Enter a valid number.");
        if (number < Minimum || number > Maximum)
            return EditorResult.Reject("The number is outside the allowed range.");
        if (!AllowDecimals && decimal.Truncate(number) != number)
            return EditorResult.Reject("Enter a whole number.");
        return EditorResult.Accept(number);
    }
}
