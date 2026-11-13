using System.Collections.Immutable;
using Em.Documents.Model;

namespace Em.Documents.Layout;

/// <summary>An RGB color used by drawing commands.</summary>
/// <param name="Red">The red channel.</param>
/// <param name="Green">The green channel.</param>
/// <param name="Blue">The blue channel.</param>
public readonly record struct DocumentColor(byte Red, byte Green, byte Blue)
{
    /// <summary>Gets the default text color, a near black.</summary>
    public static DocumentColor Black => new(25, 28, 32);
    /// <summary>Gets the default grid line color.</summary>
    public static DocumentColor Outline => new(196, 198, 208);
}

/// <summary>Base of the drawing commands that describe a laid-out page.</summary>
public abstract record DrawCommand;
/// <summary>Fills a rectangle.</summary>
/// <param name="X">The left edge in points.</param>
/// <param name="Y">The top edge in points.</param>
/// <param name="Width">The width in points.</param>
/// <param name="Height">The height in points.</param>
/// <param name="Color">The fill color.</param>
public sealed record FillRectangle(double X, double Y, double Width, double Height, DocumentColor Color) : DrawCommand;
/// <summary>Draws a straight line.</summary>
/// <param name="X1">The start x in points.</param>
/// <param name="Y1">The start y in points.</param>
/// <param name="X2">The end x in points.</param>
/// <param name="Y2">The end y in points.</param>
/// <param name="Width">The stroke width in points.</param>
/// <param name="Color">The stroke color.</param>
public sealed record DrawLine(double X1, double Y1, double X2, double Y2, double Width, DocumentColor Color) : DrawCommand;
/// <summary>Placeholder for the document's page total, drawn left-aligned at the baseline once the total is known.</summary>
public sealed record DrawPageTotal(double X, double Baseline, double Size, DocumentColor Color) : DrawCommand;
/// <summary>A shaped glyph.</summary>
/// <param name="Id">The glyph id in the font.</param>
/// <param name="Unicode">The code point the glyph represents.</param>
/// <param name="Advance">The horizontal advance in points.</param>
public readonly record struct PositionedGlyph(ushort Id, int Unicode, double Advance);
/// <summary>Draws a run of shaped glyphs on one baseline.</summary>
/// <param name="X">The start x in points.</param>
/// <param name="Baseline">The baseline y in points.</param>
/// <param name="Size">The font size in points.</param>
/// <param name="Color">The text color.</param>
/// <param name="Glyphs">The glyphs in drawing order.</param>
public sealed record DrawGlyphs(double X, double Baseline, double Size, DocumentColor Color,
    ImmutableArray<PositionedGlyph> Glyphs) : DrawCommand
{
    /// <summary>The face the glyph ids belong to; <see langword="null"/> means the bundled default face.</summary>
    public Em.Documents.Fonts.TrueTypeFace? Face { get; init; }

    /// <summary>Draw the glyphs emboldened, because the face has no bold style of its own.</summary>
    public bool SyntheticBold { get; init; }

    /// <summary>Draw the glyphs slanted, because the face has no italic style of its own.</summary>
    public bool SyntheticItalic { get; init; }
}

/// <summary>One laid-out page.</summary>
/// <param name="Number">The one-based page number.</param>
/// <param name="Setup">The page setup.</param>
/// <param name="Commands">The drawing commands in painting order.</param>
public sealed record DocumentPage(int Number, PageSetup Setup, ImmutableArray<DrawCommand> Commands);
/// <summary>The result of a pagination pass.</summary>
/// <param name="PageCount">The number of pages.</param>
/// <param name="Characters">The glyph ids used by the document by code point, for font subsetting.</param>
public sealed record PaginationPlan(int PageCount, ImmutableSortedDictionary<int, ushort> Characters);

/// <summary>Measures and shapes text for layout.</summary>
public interface ITextMeasurer
{
    /// <summary>Returns the line height for a font size.</summary>
    /// <param name="size">The font size in points.</param>
    /// <returns>The line height in points.</returns>
    double LineHeight(double size);
    /// <summary>Shapes text into positioned glyphs.</summary>
    /// <param name="text">The text to shape.</param>
    /// <param name="size">The font size in points.</param>
    /// <returns>The glyphs in drawing order.</returns>
    ImmutableArray<PositionedGlyph> Shape(string text, double size);
}

/// <summary>
/// Where a page starts: a block of a section and, inside a table, the row and the number of that row's lines
/// already placed on earlier pages. A row of -1 starts the block on a fresh page.
/// </summary>
public readonly record struct PageCheckpoint(int Section, int Block, long Row, int ConsumedLines, bool RepeatHeader);

/// <summary>
/// Page start checkpoints recorded while a document is paginated, so a preview can lay out any page directly.
/// Safe to read on one thread while another thread records.
/// </summary>
public sealed class PageIndex
{
    private readonly List<PageCheckpoint?> _starts = [];
    private readonly object _sync = new();

    /// <summary>Gets the number of pages recorded so far.</summary>
    public int Count
    {
        get { lock (_sync) return _starts.Count; }
    }

    internal void Record(int pageNumber, PageCheckpoint? checkpoint)
    {
        lock (_sync)
            if (pageNumber == _starts.Count + 1)
                _starts.Add(checkpoint);
    }

    /// <summary>Finds the nearest resumable checkpoint at or before <paramref name="pageNumber"/>.</summary>
    public bool TryFind(int pageNumber, out int startPage, out PageCheckpoint checkpoint)
    {
        lock (_sync)
        {
            for (int page = Math.Min(pageNumber, _starts.Count); page >= 1; page--)
                if (_starts[page - 1] is { } found)
                {
                    startPage = page;
                    checkpoint = found;
                    return true;
                }
        }
        startPage = 0;
        checkpoint = default;
        return false;
    }
}
