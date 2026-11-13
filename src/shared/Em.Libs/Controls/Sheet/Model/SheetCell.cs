// Ported from GridCraft (model/src/cell.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using Em.Controls.Sheet.Cells;

namespace Em.Controls.Sheet.Model;

/// <summary>Horizontal alignment of a sheet cell.</summary>
public enum CellAlignment
{
    /// <summary>Numbers align to the end, text to the start and booleans to the center.</summary>
    Auto,
    /// <summary>Aligned to the start edge.</summary>
    Start,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Aligned to the end edge.</summary>
    End
}
/// <summary>Vertical alignment of a sheet cell.</summary>
public enum CellVerticalAlignment
{
    /// <summary>Aligned to the top.</summary>
    Top,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Aligned to the bottom.</summary>
    Bottom
}

/// <summary>Which cell edges draw a border.</summary>
[Flags]
public enum CellBorders
{
    /// <summary>No border.</summary>
    None = 0,
    /// <summary>The left edge.</summary>
    Left = 1,
    /// <summary>The top edge.</summary>
    Top = 2,
    /// <summary>The right edge.</summary>
    Right = 4,
    /// <summary>The bottom edge.</summary>
    Bottom = 8,
    /// <summary>All four edges.</summary>
    All = 15
}

/// <summary>The format of sheet cells: font, colors, borders, alignment and number format. Workbooks store each distinct style once and cells refer to it by index.</summary>
public sealed record SheetStyle
{
    /// <summary>Gets the font family. The default is Roboto.</summary>
    public string FontFamily { get; init; } = "Roboto";
    /// <summary>Gets the font size in device-independent pixels. The default is 13.</summary>
    public double FontSize { get; init; } = 13;
    /// <summary>Gets whether the text is bold.</summary>
    public bool Bold { get; init; }
    /// <summary>Gets whether the text is italic.</summary>
    public bool Italic { get; init; }
    /// <summary>Gets whether the text is underlined.</summary>
    public bool Underline { get; init; }
    /// <summary>Gets whether the text is struck through.</summary>
    public bool Strike { get; init; }
    /// <summary>Gets the text color as 0xAARRGGBB, or null for the theme text color.</summary>
    public uint? TextColor { get; init; }
    /// <summary>Gets the fill color as 0xAARRGGBB, or null for no fill.</summary>
    public uint? FillColor { get; init; }
    /// <summary>Gets the border color as 0xAARRGGBB, or null for the theme outline color.</summary>
    public uint? BorderColor { get; init; }
    /// <summary>Gets the edges that draw a border.</summary>
    public CellBorders Borders { get; init; }
    /// <summary>Gets the horizontal alignment.</summary>
    public CellAlignment Alignment { get; init; }
    /// <summary>Gets the vertical alignment. The default is centered.</summary>
    public CellVerticalAlignment VerticalAlignment { get; init; } = CellVerticalAlignment.Center;
    /// <summary>Gets whether text wraps inside the cell.</summary>
    public bool Wrap { get; init; }
    /// <summary>Gets the indent level of the text.</summary>
    public int Indent { get; init; }
    /// <summary>Gets the Excel number format code. The default is General.</summary>
    public string NumberFormat { get; init; } = "General";
}

/// <summary>One stored sheet cell.</summary>
/// <param name="Value">The constant value, or the last computed value of a formula.</param>
/// <param name="Formula">The formula without the leading equals sign, or null for a constant.</param>
/// <param name="StyleIndex">The index of the cell style in the workbook style table.</param>
public readonly record struct SheetCell(CellValue Value, string? Formula = null, int StyleIndex = 0)
{
    /// <summary>Gets whether the cell has no value, no formula and the default style.</summary>
    public bool IsEmpty => Value.Kind == CellValueKind.Blank && Formula is null && StyleIndex == 0;
    /// <summary>Gets the text the user edits: the formula with a leading equals sign, or the value text.</summary>
    public string Input => Formula is null ? Value.ToString() : "=" + Formula;

    /// <summary>Creates a cell from typed input; text that starts with an equals sign becomes a formula.</summary>
    /// <param name="input">The typed text.</param>
    /// <param name="styleIndex">The style index to keep.</param>
    /// <returns>The cell.</returns>
    public static SheetCell FromInput(string input, int styleIndex = 0) =>
        input.Length > 1 && input[0] == '='
            ? new(default, input[1..], styleIndex)
            : new(CellValue.Parse(input), StyleIndex: styleIndex);
}
