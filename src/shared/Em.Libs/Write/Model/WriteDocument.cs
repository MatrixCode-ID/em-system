// Ported from WordCraft (doc/src/document.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;

namespace Em.Write.Model;

/// <summary>Horizontal paragraph alignment.</summary>
public enum WriteAlignment
{
    /// <summary>Aligned to the left indent.</summary>
    Left,
    /// <summary>Centered between the indents.</summary>
    Center,
    /// <summary>Aligned to the right indent.</summary>
    Right,
    /// <summary>Stretched to both indents, except the last line.</summary>
    Justify
}
/// <summary>Vertical position of characters.</summary>
public enum WriteBaseline
{
    /// <summary>On the baseline.</summary>
    Normal,
    /// <summary>Raised and smaller.</summary>
    Superscript,
    /// <summary>Lowered and smaller.</summary>
    Subscript
}
/// <summary>A field whose text is computed during layout.</summary>
public enum WriteField
{
    /// <summary>The current page number.</summary>
    Page,
    /// <summary>The total number of pages.</summary>
    PageCount
}
/// <summary>A text flow of a section.</summary>
public enum WriteStory
{
    /// <summary>The main text.</summary>
    Body,
    /// <summary>The default page header.</summary>
    Header,
    /// <summary>The default page footer.</summary>
    Footer,
    /// <summary>The header of the first page when it differs.</summary>
    FirstHeader,
    /// <summary>The footer of the first page when it differs.</summary>
    FirstFooter,
    /// <summary>The header of even pages when they differ.</summary>
    EvenHeader,
    /// <summary>The footer of even pages when they differ.</summary>
    EvenFooter
}
/// <summary>The marker style of a list.</summary>
public enum WriteListKind
{
    /// <summary>A bullet character.</summary>
    Bullet,
    /// <summary>An increasing number.</summary>
    Decimal
}
/// <summary>What a named style formats.</summary>
public enum WriteStyleKind
{
    /// <summary>Whole paragraphs, including their character defaults.</summary>
    Paragraph,
    /// <summary>Runs of characters.</summary>
    Character
}

/// <summary>How text lines up at a tab stop. <see cref="Bar"/> draws a vertical line and does not stop text.</summary>
/// <summary>How text aligns at a tab stop.</summary>
public enum WriteTabAlignment
{
    /// <summary>Text starts at the stop.</summary>
    Left,
    /// <summary>Text is centered on the stop.</summary>
    Center,
    /// <summary>Text ends at the stop.</summary>
    Right,
    /// <summary>The decimal separator aligns on the stop.</summary>
    Decimal,
    /// <summary>A vertical bar is drawn at the stop.</summary>
    Bar
}

/// <summary>A tab stop, <paramref name="Position"/> device-independent pixels from the left edge of the text column.</summary>
public readonly record struct WriteTabStop(double Position, WriteTabAlignment Alignment = WriteTabAlignment.Left)
{
    /// <summary>Two stops closer than this many device-independent pixels are the same stop.</summary>
    public const double Tolerance = .5;

    /// <summary>The spacing of the default stops that apply past the last set stop: half an inch.</summary>
    public const double DefaultInterval = 48;
}

/// <summary>Character formatting. Null properties inherit from the style or the paragraph defaults.</summary>
public sealed record WriteCharacterStyle
{
    /// <summary>Gets the font family name.</summary>
    public string? FontFamily { get; init; }
    /// <summary>Gets the font size in device-independent pixels.</summary>
    public double? FontSize { get; init; }
    /// <summary>Gets whether the text is bold.</summary>
    public bool? Bold { get; init; }
    /// <summary>Gets whether the text is italic.</summary>
    public bool? Italic { get; init; }
    /// <summary>Gets whether the text is underlined.</summary>
    public bool? Underline { get; init; }
    /// <summary>Gets whether the text is struck through.</summary>
    public bool? Strike { get; init; }
    /// <summary>Gets the text color as 0xRRGGBB.</summary>
    public uint? Color { get; init; }
    /// <summary>Gets the highlight color as 0xRRGGBB.</summary>
    public uint? Highlight { get; init; }
    /// <summary>Gets the vertical position of the characters.</summary>
    public WriteBaseline? Baseline { get; init; }

    /// <summary>Layers another style over this one: each property set in <paramref name="style"/> replaces the value here.</summary>
    /// <param name="style">The overriding style.</param>
    /// <returns>The combined style.</returns>
    public WriteCharacterStyle Overlay(WriteCharacterStyle style) => new()
    {
        FontFamily = style.FontFamily ?? FontFamily, FontSize = style.FontSize ?? FontSize,
        Bold = style.Bold ?? Bold, Italic = style.Italic ?? Italic, Underline = style.Underline ?? Underline,
        Strike = style.Strike ?? Strike, Color = style.Color ?? Color, Highlight = style.Highlight ?? Highlight,
        Baseline = style.Baseline ?? Baseline
    };
}

/// <summary>Paragraph formatting. Null properties inherit from the style or the document defaults. Distances are in device-independent pixels.</summary>
public sealed record WriteParagraphStyle
{
    /// <summary>Gets the horizontal alignment.</summary>
    public WriteAlignment? Alignment { get; init; }
    /// <summary>Gets the left indent in device-independent pixels.</summary>
    public double? LeftIndent { get; init; }
    /// <summary>Gets the right indent in device-independent pixels.</summary>
    public double? RightIndent { get; init; }
    /// <summary>Gets the first line indent in device-independent pixels; negative values create a hanging indent.</summary>
    public double? FirstLineIndent { get; init; }
    /// <summary>Gets the space before the paragraph in device-independent pixels.</summary>
    public double? SpaceBefore { get; init; }
    /// <summary>Gets the space after the paragraph in device-independent pixels.</summary>
    public double? SpaceAfter { get; init; }
    /// <summary>Gets the line spacing as a multiple of single spacing.</summary>
    public double? LineSpacing { get; init; }
    /// <summary>Gets whether the paragraph stays on the same page as the next one.</summary>
    public bool? KeepWithNext { get; init; }
    /// <summary>Gets whether the paragraph starts on a new page.</summary>
    public bool? PageBreakBefore { get; init; }
    /// <summary>Gets whether single first and last lines are kept off page boundaries.</summary>
    public bool? WidowControl { get; init; }

    /// <summary>The paragraph's tab stops, ordered by position; <see langword="null"/> inherits them from the style.</summary>
    public ImmutableArray<WriteTabStop>? TabStops { get; init; }

    /// <summary>Layers another style over this one: each property set in <paramref name="style"/> replaces the value here.</summary>
    /// <param name="style">The overriding style.</param>
    /// <returns>The combined style.</returns>
    public WriteParagraphStyle Overlay(WriteParagraphStyle style) => new()
    {
        TabStops = style.TabStops ?? TabStops,
        Alignment = style.Alignment ?? Alignment, LeftIndent = style.LeftIndent ?? LeftIndent,
        RightIndent = style.RightIndent ?? RightIndent, FirstLineIndent = style.FirstLineIndent ?? FirstLineIndent,
        SpaceBefore = style.SpaceBefore ?? SpaceBefore, SpaceAfter = style.SpaceAfter ?? SpaceAfter,
        LineSpacing = style.LineSpacing ?? LineSpacing, KeepWithNext = style.KeepWithNext ?? KeepWithNext,
        PageBreakBefore = style.PageBreakBefore ?? PageBreakBefore, WidowControl = style.WidowControl ?? WidowControl
    };
}

/// <summary>A named paragraph or character style.</summary>
/// <param name="Id">The style identifier used by paragraphs and runs.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">Whether the style formats paragraphs or characters.</param>
/// <param name="BasedOn">The identifier of the style this one inherits from, or null.</param>
/// <param name="Paragraph">The paragraph formatting, for paragraph styles.</param>
/// <param name="Characters">The character formatting.</param>
public sealed record WriteStyle(string Id, string Name, WriteStyleKind Kind, string? BasedOn = null,
    WriteParagraphStyle? Paragraph = null, WriteCharacterStyle? Characters = null);

/// <summary>A list definition referenced by paragraphs.</summary>
/// <param name="Id">The numbering identifier.</param>
/// <param name="Kind">The marker style.</param>
/// <param name="Start">The first number.</param>
/// <param name="Label">The bullet character, or a number pattern in which %1 stands for the number.</param>
public sealed record WriteNumbering(int Id, WriteListKind Kind, int Start = 1, string? Label = null)
{
    /// <summary>Returns the marker of a list item.</summary>
    /// <param name="index">The zero-based position of the item in the list.</param>
    /// <returns>The bullet, or the formatted number.</returns>
    public string Format(int index) => Kind == WriteListKind.Bullet ? Label ?? "•" :
        (Label ?? "%1.").Replace("%1", (Start + index).ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>An embedded picture.</summary>
/// <param name="Bytes">The image file bytes.</param>
/// <param name="ContentType">The MIME type of the image.</param>
/// <param name="Width">The display width in device-independent pixels.</param>
/// <param name="Height">The display height in device-independent pixels.</param>
/// <param name="Description">The alternative text, or null.</param>
public sealed record WriteImage(ImmutableArray<byte> Bytes, string ContentType, double Width, double Height, string? Description = null);

/// <summary>A run of text with uniform formatting, or one embedded object.</summary>
/// <param name="Text">The run text.</param>
/// <param name="Format">Direct character formatting, or null.</param>
/// <param name="StyleId">The character style identifier, or null.</param>
/// <param name="Field">A page field shown instead of the text, or null.</param>
/// <param name="Image">An image shown instead of the text, or null.</param>
/// <param name="OpaqueXml">Unsupported content kept as XML so it survives a save, or null.</param>
public sealed record WriteRun(string Text, WriteCharacterStyle? Format = null, string? StyleId = null,
    WriteField? Field = null, WriteImage? Image = null, string? OpaqueXml = null)
{
    /// <summary>Gets the number of positions the run occupies: 1 for a field, image or opaque object, otherwise the text length.</summary>
    public int Length => Field is not null || Image is not null || OpaqueXml is not null ? 1 : Text.Length;
    /// <summary>Gets the text used for searching and copying; objects become the object replacement character.</summary>
    public string PlainText => Field is not null || Image is not null || OpaqueXml is not null ? "\uFFFC" : Text;
}

/// <summary>Base of the blocks of a story: paragraphs, tables, page breaks and preserved content.</summary>
public abstract record WriteBlock;
/// <summary>A paragraph of runs.</summary>
/// <param name="Runs">The runs in order.</param>
/// <param name="StyleId">The paragraph style identifier.</param>
/// <param name="Format">Direct paragraph formatting, or null.</param>
/// <param name="NumberingId">The list the paragraph belongs to, or null.</param>
/// <param name="Mark">The character formatting of the paragraph mark, used for empty paragraphs and new text.</param>
public sealed record WriteParagraph(ImmutableArray<WriteRun> Runs, string StyleId = "Normal",
    WriteParagraphStyle? Format = null, int? NumberingId = null, WriteCharacterStyle? Mark = null) : WriteBlock
{
    /// <summary>Creates a paragraph with one unformatted run.</summary>
    /// <param name="text">The paragraph text; empty text creates an empty paragraph.</param>
    public WriteParagraph(string text = "") : this(text.Length == 0 ? [] : [new WriteRun(text)]) { }
    /// <summary>Gets the plain text of the runs.</summary>
    public string Text => string.Concat(Runs.Select(run => run.PlainText));
    /// <summary>Gets the number of positions in the paragraph.</summary>
    public int Length => Runs.Sum(run => run.Length);
}
/// <summary>A hard page break.</summary>
public sealed record WritePageBreak : WriteBlock;
/// <summary>Unsupported block content kept as XML so it survives a save.</summary>
/// <param name="Xml">The original XML.</param>
public sealed record WriteOpaqueBlock(string Xml) : WriteBlock;

/// <summary>A table cell.</summary>
/// <param name="Blocks">The cell content.</param>
/// <param name="ColumnSpan">The number of grid columns the cell spans.</param>
/// <param name="Fill">The fill color as 0xRRGGBB, or null.</param>
/// <param name="Border">The border color as 0xRRGGBB, or null.</param>
/// <param name="RowSpan">The number of rows the cell spans.</param>
/// <param name="ContinuesMerge">True when the cell is covered by a vertical merge from the row above.</param>
/// <param name="NoBorder">True to draw no border.</param>
public sealed record WriteTableCell(ImmutableArray<WriteBlock> Blocks, int ColumnSpan = 1,
    uint? Fill = null, uint? Border = null, int RowSpan = 1, bool ContinuesMerge = false, bool NoBorder = false)
{
    /// <summary>Creates a cell with one paragraph of text.</summary>
    /// <param name="text">The cell text.</param>
    public WriteTableCell(string text = "") : this([new WriteParagraph(text)]) { }
}

/// <summary>A table row.</summary>
/// <param name="Cells">The cells in order.</param>
/// <param name="RepeatHeader">Whether the row repeats at the top of every page the table continues on.</param>
public sealed record WriteTableRow(ImmutableArray<WriteTableCell> Cells, bool RepeatHeader = false);
/// <summary>A table.</summary>
/// <param name="ColumnWidths">The grid column widths in device-independent pixels.</param>
/// <param name="Rows">The rows in order.</param>
public sealed record WriteTable(ImmutableArray<double> ColumnWidths, ImmutableArray<WriteTableRow> Rows) : WriteBlock;

/// <summary>Page size, margins and header options of a section, in device-independent pixels (1/96 inch). The defaults are US Letter with one-inch margins.</summary>
/// <param name="Width">The page width.</param>
/// <param name="Height">The page height.</param>
/// <param name="Left">The left margin.</param>
/// <param name="Top">The top margin.</param>
/// <param name="Right">The right margin.</param>
/// <param name="Bottom">The bottom margin.</param>
/// <param name="HeaderDistance">The distance from the top edge to the header.</param>
/// <param name="FooterDistance">The distance from the bottom edge to the footer.</param>
/// <param name="DifferentFirstPage">Whether the first page uses the first-page header and footer.</param>
/// <param name="DifferentEvenPages">Whether even pages use the even-page header and footer.</param>
public sealed record WritePageSettings(double Width = 816, double Height = 1056,
    double Left = 96, double Top = 96, double Right = 96, double Bottom = 96,
    double HeaderDistance = 48, double FooterDistance = 48, bool DifferentFirstPage = false, bool DifferentEvenPages = false);

/// <summary>A section of the document with its own page settings.</summary>
/// <param name="Blocks">The body blocks.</param>
/// <param name="Page">The page settings.</param>
public sealed record WriteSection(ImmutableArray<WriteBlock> Blocks, WritePageSettings Page)
{
    /// <summary>Gets the header and footer stories of the section.</summary>
    public ImmutableDictionary<WriteStory, ImmutableArray<WriteBlock>> Stories { get; init; } =
        ImmutableDictionary<WriteStory, ImmutableArray<WriteBlock>>.Empty;
}

/// <summary>An immutable word-processing document. Edit it through a <see cref="Em.Write.Editing.WriteSession"/>.</summary>
/// <param name="Sections">The sections in order.</param>
/// <param name="Styles">The named styles by identifier.</param>
/// <param name="Numbering">The list definitions by identifier.</param>
public sealed record WriteDocument(ImmutableArray<WriteSection> Sections,
    ImmutableDictionary<string, WriteStyle> Styles, ImmutableDictionary<int, WriteNumbering> Numbering)
{
    private static readonly Lazy<ImmutableDictionary<string, WriteStyle>> Defaults = new(CreateStyles);

    /// <summary>Creates a document with the default styles, one section and one paragraph per line of text.</summary>
    /// <param name="text">The initial text.</param>
    /// <returns>The document.</returns>
    public static WriteDocument Create(string text = "") => new(
        [new([.. text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n')
            .Select(line => (WriteBlock)new WriteParagraph(line))], new())],
        Defaults.Value, ImmutableDictionary<int, WriteNumbering>.Empty);

    /// <summary>Resolves the effective formatting of a paragraph from its style chain and direct formatting.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <returns>The paragraph formatting and the default character formatting.</returns>
    public (WriteParagraphStyle Paragraph, WriteCharacterStyle Characters) Resolve(WriteParagraph paragraph)
    {
        var result = ResolveStyle(paragraph.StyleId);
        return (result.Paragraph.Overlay(paragraph.Format ?? new()), result.Characters);
    }

    /// <summary>Resolves the effective formatting of a run from the paragraph, the run style and the direct formatting.</summary>
    /// <param name="paragraph">The paragraph that holds the run.</param>
    /// <param name="run">The run.</param>
    /// <returns>The character formatting.</returns>
    public WriteCharacterStyle Resolve(WriteParagraph paragraph, WriteRun run) =>
        Resolve(paragraph).Characters.Overlay(ResolveStyle(run.StyleId).Characters).Overlay(run.Format ?? new());

    private (WriteParagraphStyle Paragraph, WriteCharacterStyle Characters) ResolveStyle(string? styleId)
    {
        var chain = new Stack<WriteStyle>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (styleId is not null && Styles.TryGetValue(styleId, out var item) && seen.Add(styleId))
        {
            chain.Push(item);
            styleId = item.BasedOn;
        }
        var paragraph = new WriteParagraphStyle();
        var characters = new WriteCharacterStyle();
        foreach (var item in chain)
        {
            if (item.Paragraph is not null)
                paragraph = paragraph.Overlay(item.Paragraph);
            if (item.Characters is not null)
                characters = characters.Overlay(item.Characters);
        }
        return (paragraph, characters);
    }

    private static ImmutableDictionary<string, WriteStyle> CreateStyles()
    {
        var styles = ImmutableDictionary.CreateBuilder<string, WriteStyle>(StringComparer.OrdinalIgnoreCase);
        styles.Add("Normal", new("Normal", "Normal", WriteStyleKind.Paragraph, Characters: new() { FontFamily = "Noto Sans", FontSize = 14.667 }));
        styles.Add("Title", new("Title", "Title", WriteStyleKind.Paragraph, "Normal", Characters: new() { FontSize = 32 }));
        styles.Add("ListParagraph", new("ListParagraph", "List Paragraph", WriteStyleKind.Paragraph, "Normal",
            new() { LeftIndent = 36, FirstLineIndent = -18 }));
        styles.Add("Emphasis", new("Emphasis", "Emphasis", WriteStyleKind.Character, Characters: new() { Italic = true }));
        for (int i = 1; i <= 6; i++)
            styles.Add($"Heading{i}", new($"Heading{i}", $"Heading {i}", WriteStyleKind.Paragraph, "Normal",
                new() { KeepWithNext = true, SpaceBefore = 12, SpaceAfter = 6 }, new() { Bold = true, FontSize = 28 - i * 2 }));
        return styles.ToImmutable();
    }
}
