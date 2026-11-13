// Ported from GridCraft (core/src/value.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Globalization;

namespace Em.Controls.Sheet.Cells;

/// <summary>The kind of a <see cref="CellValue"/>.</summary>
public enum CellValueKind
{
    /// <summary>An empty cell.</summary>
    Blank,
    /// <summary>A double-precision number, also used for dates.</summary>
    Number,
    /// <summary>Text.</summary>
    Text,
    /// <summary>TRUE or FALSE.</summary>
    Boolean,
    /// <summary>A formula error.</summary>
    Error
}
/// <summary>A spreadsheet error value.</summary>
public enum CellError
{
    /// <summary><c>#NULL!</c>, an empty range intersection.</summary>
    Null,
    /// <summary><c>#DIV/0!</c>.</summary>
    DivideByZero,
    /// <summary><c>#VALUE!</c>, an argument of the wrong type.</summary>
    Value,
    /// <summary><c>#REF!</c>, an invalid reference.</summary>
    Reference,
    /// <summary><c>#NAME?</c>, an unknown function or name.</summary>
    Name,
    /// <summary><c>#NUM!</c>, an invalid number.</summary>
    Number,
    /// <summary><c>#N/A</c>, a value that is not available.</summary>
    NotAvailable,
    /// <summary><c>#CIRC!</c>, a circular reference.</summary>
    Circular
}
/// <summary>The date system that maps serial numbers to dates.</summary>
public enum SheetDateSystem
{
    /// <summary>Serial 1 is 1 January 1900, with Excel's 29 February 1900.</summary>
    Excel1900,
    /// <summary>Serial 0 is 1 January 1904.</summary>
    Excel1904
}

/// <summary>An immutable spreadsheet value: blank, number, text, boolean or error.</summary>
public readonly record struct CellValue
{
    private CellValue(CellValueKind kind, double number = 0, string? text = null, CellError error = default)
        => (Kind, Number, Text, Error) = (kind, number, text, error);

    /// <summary>Gets the kind of value.</summary>
    public CellValueKind Kind { get; }
    /// <summary>Gets the number; booleans store 1 or 0.</summary>
    public double Number { get; }
    /// <summary>Gets the text, or null when the value is not text.</summary>
    public string? Text { get; }
    /// <summary>Gets the error, meaningful when <see cref="Kind"/> is <c>Error</c>.</summary>
    public CellError Error { get; }
    /// <summary>Gets whether the stored number is non-zero.</summary>
    public bool Boolean => Number != 0;

    /// <summary>Creates a number value; infinities and NaN become the <c>#NUM!</c> error.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The value.</returns>
    public static CellValue From(double value) => double.IsFinite(value) ? new(CellValueKind.Number, value) : From(CellError.Number);
    /// <summary>Creates a text value.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The value.</returns>
    public static CellValue From(string value) => new(CellValueKind.Text, text: value);
    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">The boolean.</param>
    /// <returns>The value.</returns>
    public static CellValue From(bool value) => new(CellValueKind.Boolean, value ? 1 : 0);
    /// <summary>Creates an error value.</summary>
    /// <param name="value">The error.</param>
    /// <returns>The value.</returns>
    public static CellValue From(CellError value) => new(CellValueKind.Error, error: value);

    /// <summary>Returns the invariant text of the value, using Excel spellings for booleans and errors.</summary>
    /// <returns>The value text.</returns>
    public override string ToString() => Kind switch
    {
        CellValueKind.Number => Number.ToString("G15", CultureInfo.InvariantCulture),
        CellValueKind.Text => Text!,
        CellValueKind.Boolean => Boolean ? "TRUE" : "FALSE",
        CellValueKind.Error => Error switch
        {
            CellError.Null => "#NULL!", CellError.DivideByZero => "#DIV/0!", CellError.Value => "#VALUE!",
            CellError.Reference => "#REF!", CellError.Name => "#NAME?", CellError.Number => "#NUM!",
            CellError.NotAvailable => "#N/A", _ => "#CIRC!"
        },
        _ => ""
    };

    /// <summary>Parses invariant input text: empty text is blank, a leading apostrophe forces text, then booleans and numbers are recognized.</summary>
    /// <param name="text">The input text.</param>
    /// <returns>The value.</returns>
    public static CellValue Parse(string text)
    {
        if (text.Length == 0)
            return default;
        if (text[0] == '\'')
            return From(text[1..]);
        if (bool.TryParse(text, out bool boolean))
            return From(boolean);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
            double.IsFinite(number))
            return From(number);
        return From(text);
    }
}
