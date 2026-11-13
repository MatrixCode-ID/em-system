using System.Collections.Immutable;
using System.Globalization;

namespace Em.Documents.Model;

/// <summary>The kind of value held by a <see cref="DocumentCell"/>.</summary>
public enum CellKind
{
    /// <summary>No value.</summary>
    Empty,
    /// <summary>Text.</summary>
    Text,
    /// <summary>A decimal number.</summary>
    Number,
    /// <summary>A date without a time.</summary>
    Date,
    /// <summary>A boolean value.</summary>
    Boolean
}
/// <summary>Horizontal alignment of a document column.</summary>
public enum HorizontalAlignment
{
    /// <summary>Aligned to the left edge.</summary>
    Left,
    /// <summary>Centered.</summary>
    Center,
    /// <summary>Aligned to the right edge.</summary>
    Right
}
/// <summary>The role of a <see cref="DocumentRow"/>.</summary>
public enum DocumentRowKind
{
    /// <summary>A data row.</summary>
    Data,
    /// <summary>A group header row.</summary>
    Group,
    /// <summary>A summary row, such as a group footer or the grand total.</summary>
    Summary
}
/// <summary>Page orientation.</summary>
public enum PageOrientation
{
    /// <summary>The longer side is vertical.</summary>
    Portrait,
    /// <summary>The longer side is horizontal.</summary>
    Landscape
}

/// <summary>One cell of a document table.</summary>
/// <param name="Kind">The kind of value.</param>
/// <param name="Value">The typed value written to spreadsheets.</param>
/// <param name="DisplayText">The text shown in printed and text formats; null formats <see cref="Value"/>.</param>
public readonly record struct DocumentCell(CellKind Kind, object? Value, string? DisplayText = null)
{
    internal bool? CheckMark { get; init; }

    /// <summary>Creates a text cell; null creates an empty cell.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The cell.</returns>
    public static DocumentCell Text(string? text) => text is null ? default : new(CellKind.Text, text);
    /// <summary>Creates a number cell.</summary>
    /// <param name="value">The number.</param>
    /// <param name="display">Optional display text.</param>
    /// <returns>The cell.</returns>
    public static DocumentCell Number(decimal value, string? display = null) => new(CellKind.Number, value, display);
    /// <summary>Creates a date cell.</summary>
    /// <param name="value">The date.</param>
    /// <param name="display">Optional display text.</param>
    /// <returns>The cell.</returns>
    public static DocumentCell Date(DateOnly value, string? display = null) => new(CellKind.Date, value, display);
    /// <summary>Creates a boolean cell.</summary>
    /// <param name="value">The boolean.</param>
    /// <param name="display">Optional display text.</param>
    /// <returns>The cell.</returns>
    public static DocumentCell Boolean(bool value, string? display = null) => new(CellKind.Boolean, value, display);

    /// <summary>Returns the cell text: <see cref="DisplayText"/> when set, otherwise the formatted value.</summary>
    /// <param name="culture">The culture for numbers and dates.</param>
    /// <param name="format">An optional .NET format string; dates default to the short date pattern.</param>
    /// <returns>The display text.</returns>
    public string Format(CultureInfo culture, string? format = null) => DisplayText ?? (Value switch
    {
        DateOnly date => date.ToString(format ?? "d", culture),
        IFormattable formattable => formattable.ToString(format, culture),
        _ => Value?.ToString() ?? string.Empty
    });
}

/// <summary>A column of a document table.</summary>
/// <param name="Title">The header caption.</param>
/// <param name="Width">The column width in points.</param>
/// <param name="Alignment">The horizontal alignment of cells.</param>
/// <param name="DisplayFormat">The .NET format for printed cells, or null.</param>
/// <param name="SpreadsheetFormat">The number format written to spreadsheets, or null.</param>
public sealed record DocumentColumn(string Title, double Width, HorizontalAlignment Alignment = HorizontalAlignment.Left,
    string? DisplayFormat = null, string? SpreadsheetFormat = null);

/// <summary>A row of a document table.</summary>
/// <param name="Cells">The cells, one per column.</param>
/// <param name="Kind">The role of the row.</param>
/// <param name="OutlineLevel">The group nesting level used for spreadsheet outlines and indentation.</param>
public sealed record DocumentRow(ImmutableArray<DocumentCell> Cells, DocumentRowKind Kind = DocumentRowKind.Data,
    int OutlineLevel = 0);

/// <summary>Paper size and margins in points. Width and height describe the sheet; <see cref="Orientation"/> decides which side is horizontal.</summary>
/// <param name="Width">The sheet width in points; the default is A4.</param>
/// <param name="Height">The sheet height in points; the default is A4.</param>
/// <param name="Left">The left margin.</param>
/// <param name="Top">The top margin.</param>
/// <param name="Right">The right margin.</param>
/// <param name="Bottom">The bottom margin.</param>
/// <param name="Orientation">The page orientation.</param>
public sealed record PageSetup(double Width = 595.28, double Height = 841.89, double Left = 36,
    double Top = 36, double Right = 36, double Bottom = 36, PageOrientation Orientation = PageOrientation.Portrait)
{
    /// <summary>Gets the oriented page width in points.</summary>
    public double PageWidth => Orientation == PageOrientation.Landscape ? Math.Max(Width, Height) : Math.Min(Width, Height);
    /// <summary>Gets the oriented page height in points.</summary>
    public double PageHeight => Orientation == PageOrientation.Landscape ? Math.Min(Width, Height) : Math.Max(Width, Height);
    /// <summary>Gets the printable width between the left and right margins.</summary>
    public double ContentWidth => PageWidth - Left - Right;
    /// <summary>Gets the printable height between the top and bottom margins.</summary>
    public double ContentHeight => PageHeight - Top - Bottom;

    /// <summary>Checks that the values are finite, the sheet is between 36 and 14400 points on each side and the margins leave a usable area. Throws <see cref="ArgumentException"/> otherwise.</summary>
    public void Validate()
    {
        if (!new[] { Width, Height, Left, Top, Right, Bottom }.All(double.IsFinite) ||
            Width is < 36 or > 14400 || Height is < 36 or > 14400 ||
            Left < 0 || Top < 0 || Right < 0 || Bottom < 0 || ContentWidth < 24 || ContentHeight < 72)
            throw new ArgumentException("The paper size or margins leave too little usable space.");
    }
}

/// <summary>Base of the blocks that make up a document section.</summary>
public abstract record DocumentBlock;

/// <summary>A paragraph of plain text.</summary>
/// <param name="Text">The paragraph text.</param>
public sealed record DocumentParagraph(string Text) : DocumentBlock;

/// <summary>Forces the following blocks onto a new page.</summary>
public sealed record DocumentPageBreak : DocumentBlock;

/// <summary>A table whose rows are streamed when the document is written, so large views need not fit in memory.</summary>
/// <param name="Columns">The table columns.</param>
/// <param name="OpenRows">Opens a new stream of the rows; called once per pass over the table.</param>
/// <param name="RepeatHeader">Whether the header row repeats on every printed page.</param>
/// <param name="FreezeHeader">Whether spreadsheets freeze the header row.</param>
/// <param name="SheetName">The worksheet name used by spreadsheet writers.</param>
public sealed record DocumentTable(
    ImmutableArray<DocumentColumn> Columns,
    Func<CancellationToken, IAsyncEnumerable<DocumentRow>> OpenRows,
    bool RepeatHeader = true, bool FreezeHeader = true, string SheetName = "Grid") : DocumentBlock
{
    /// <summary>
    /// Optional fast path that opens the rows starting at a zero-based row position, used to lay out a page
    /// without reading the rows before it. When absent, the earlier rows are read and skipped.
    /// </summary>
    public Func<long, CancellationToken, IAsyncEnumerable<DocumentRow>>? OpenRowsFrom { get; init; }
}

/// <summary>A part of a document with its own page setup.</summary>
/// <param name="Page">The page setup.</param>
/// <param name="Blocks">The content blocks.</param>
/// <param name="Header">Optional page header text.</param>
/// <param name="Footer">Optional page footer text.</param>
/// <param name="PageNumbers">Whether page numbers are printed.</param>
public sealed record DocumentSection(PageSetup Page, ImmutableArray<DocumentBlock> Blocks,
    string? Header = null, string? Footer = null, bool PageNumbers = true)
{
    /// <summary>Gets whether the document title is printed at the top of the section. The default is true.</summary>
    public bool ShowHeading { get; init; } = true;
}

/// <summary>A renderer-neutral document that writers turn into PDF, XLSX, DOCX or CSV, and that the print preview paginates.</summary>
/// <param name="Title">The document title.</param>
/// <param name="Sections">The sections in order.</param>
public sealed record Document(string Title, ImmutableArray<DocumentSection> Sections)
{
    /// <summary>Gets the creation time written to file metadata and headings.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;
    /// <summary>Gets the culture used to format values.</summary>
    public CultureInfo Culture { get; init; } = CultureInfo.InvariantCulture;
    /// <summary>Gets whether table columns are scaled down to fit the printable width. The default is true.</summary>
    public bool FitToWidth { get; init; } = true;

    /// <summary>Receives the number of pages written so far by paged writers such as PDF.</summary>
    public IProgress<int>? PageProgress { get; init; }
}
