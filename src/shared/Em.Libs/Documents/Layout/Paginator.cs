using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using Em.Documents.Fonts;
using Em.Documents.Model;

namespace Em.Documents.Layout;

/// <summary>Lays out documents into pages of drawing commands for PDF writing and print preview.</summary>
public sealed class Paginator
{
    private const double BodySize = 9;
    private const double Padding = 5;
    private const double FooterReserve = 18;
    private readonly ITextMeasurer _measurer;

    /// <summary>Creates a paginator.</summary>
    /// <param name="measurer">The text measurer, or null to use the bundled font.</param>
    public Paginator(ITextMeasurer? measurer = null) =>
        _measurer = measurer ?? new TrueTypeMeasurer(TrueTypeFace.Default);

    /// <summary>
    /// Page count to pass when the total is not known while writing. Footers then end with "of " followed by a
    /// <see cref="DrawPageTotal"/> placeholder that a single-pass writer fills in after the last page.
    /// </summary>
    public const int DeferredPageCount = -1;

    /// <summary>Paginates the whole document once to count the pages and collect the glyphs it uses.</summary>
    /// <param name="document">The document to paginate.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>The page count and the glyphs used.</returns>
    public async Task<PaginationPlan> PlanAsync(Document document, CancellationToken cancellationToken = default)
    {
        int pages = 0;
        var characters = ImmutableSortedDictionary.CreateBuilder<int, ushort>();
        await foreach (var page in PagesAsync(document, 0, cancellationToken))
        {
            pages++;
            foreach (var run in page.Commands.OfType<DrawGlyphs>())
                foreach (var glyph in run.Glyphs)
                    characters.TryAdd(glyph.Unicode, glyph.Id);
        }
        foreach (var glyph in _measurer.Shape("Page 0123456789 of", BodySize))
            characters.TryAdd(glyph.Unicode, glyph.Id);
        return new PaginationPlan(pages, characters.ToImmutable());
    }

    /// <summary>
    /// Counts the pages without building their drawing commands, so a preview can learn the page count of a large
    /// document cheaply after its first pages are on screen. When <paramref name="pageIndex"/> is given, it records where
    /// every page starts so that any page can later be laid out without paginating the pages before it.
    /// </summary>
    public async Task<int> CountPagesAsync(Document document, IProgress<int>? progress = null,
        CancellationToken cancellationToken = default, PageIndex? pageIndex = null)
    {
        int pages = 0;
        await foreach (var page in PagesCoreAsync(document, 0, measureOnly: true, null, 1, pageIndex, cancellationToken)
            .ConfigureAwait(false))
        {
            pages = page.Number;
            if ((pages & 31) == 0)
                progress?.Report(pages);
        }
        progress?.Report(pages);
        return pages;
    }

    /// <summary>Lays out the pages from the first page on.</summary>
    /// <param name="document">The document to lay out.</param>
    /// <param name="pageCount">The total page count printed in footers, or <see cref="DeferredPageCount"/> when it is not known yet; 0 omits it.</param>
    /// <param name="cancellationToken">Cancels the layout.</param>
    /// <returns>The pages in order.</returns>
    public IAsyncEnumerable<DocumentPage> PagesAsync(Document document, int pageCount,
        CancellationToken cancellationToken = default) =>
        PagesCoreAsync(document, pageCount, measureOnly: false, null, 1, null, cancellationToken);

    /// <summary>
    /// Lays out the pages from <paramref name="startPage"/> onwards, resuming at a checkpoint recorded by
    /// <see cref="CountPagesAsync"/> or by <see cref="PagesAsync(Document, int, PageIndex?, CancellationToken)"/>.
    /// </summary>
    public IAsyncEnumerable<DocumentPage> PagesFromAsync(Document document, int pageCount, int startPage,
        PageCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(startPage, 1);
        return PagesCoreAsync(document, pageCount, measureOnly: false, checkpoint, startPage, null, cancellationToken);
    }

    /// <summary>Lays out every page and records where each page starts into <paramref name="pageIndex"/>.</summary>
    public IAsyncEnumerable<DocumentPage> PagesAsync(Document document, int pageCount, PageIndex? pageIndex,
        CancellationToken cancellationToken = default) =>
        PagesCoreAsync(document, pageCount, measureOnly: false, null, 1, pageIndex, cancellationToken);

    private async IAsyncEnumerable<DocumentPage> PagesCoreAsync(Document document, int pageCount, bool measureOnly,
        PageCheckpoint? resume, int resumePage, PageIndex? pageIndex,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        // Wrapped lines are immutable, so repeated cell texts in one column share one shaping result.
        var wraps = new WrapCache(this);
        int number = resume is null ? 0 : resumePage - 1;
        int firstSection = resume?.Section ?? 0;
        for (int sectionIndex = firstSection; sectionIndex < document.Sections.Length; sectionIndex++)
        {
            var section = document.Sections[sectionIndex];
            cancellationToken.ThrowIfCancellationRequested();
            section.Page.Validate();
            bool resumed = resume is not null && sectionIndex == firstSection;
            int firstBlock = resumed ? resume!.Value.Block : 0;
            var builder = NewPage(section, document, ++number, measureOnly);
            pageIndex?.Record(number, resumed ? resume : new PageCheckpoint(sectionIndex, 0, -1, 0, false));
            for (int blockIndex = firstBlock; blockIndex < section.Blocks.Length; blockIndex++)
            {
                var block = section.Blocks[blockIndex];
                bool resumeInTable = resumed && blockIndex == firstBlock && resume!.Value.Row >= 0;
                cancellationToken.ThrowIfCancellationRequested();
                switch (block)
                {
                    case DocumentPageBreak:
                        yield return builder.Finish(pageCount, _measurer);
                        builder = NewPage(section, document, ++number, measureOnly);
                        pageIndex?.Record(number, new PageCheckpoint(sectionIndex, blockIndex + 1, -1, 0, false));
                        break;
                    case DocumentParagraph paragraph:
                        foreach (var line in Wrap(paragraph.Text, section.Page.ContentWidth))
                        {
                            if (builder.Y + _measurer.LineHeight(BodySize) > builder.Bottom)
                            {
                                yield return builder.Finish(pageCount, _measurer);
                                builder = NewPage(section, document, ++number, measureOnly);
                                // A page that starts inside a paragraph is laid out from an earlier checkpoint.
                                pageIndex?.Record(number, null);
                            }
                            builder.DrawText(line, section.Page.Left, builder.Y + BodySize, BodySize);
                            builder.Y += _measurer.LineHeight(BodySize);
                        }
                        builder.Y += Padding;
                        break;
                    case DocumentTable table when !table.Columns.IsDefaultOrEmpty:
                        var widths = ColumnWidths(table.Columns, section.Page, document.FitToWidth);
                        var header = table.Columns.Select((column, index) =>
                            new LayoutCell(Wrap(column.Title, widths[index] - Padding * 2))).ToArray();
                        var headerLines = Math.Min(3, header.Max(cell => cell.Lines.Length));
                        wraps.Reset(table.Columns.Length);
                        long rowIndex = 0;
                        int startConsumed = 0;
                        if (resumeInTable)
                        {
                            rowIndex = resume!.Value.Row;
                            startConsumed = resume.Value.ConsumedLines;
                            if (resume.Value.RepeatHeader)
                                DrawRow(builder, table.Columns, widths, header, 0, headerLines, header: true, DocumentRowKind.Data);
                        }
                        else
                        {
                            if (!builder.CanFit(RowHeight(headerLines)))
                            {
                                yield return builder.Finish(pageCount, _measurer);
                                builder = NewPage(section, document, ++number, measureOnly);
                                pageIndex?.Record(number, new PageCheckpoint(sectionIndex, blockIndex, -1, 0, false));
                            }
                            DrawRow(builder, table.Columns, widths, header, 0, headerLines, header: true, DocumentRowKind.Data);
                        }
                        await foreach (var row in RowsFrom(table, rowIndex, cancellationToken).WithCancellation(cancellationToken))
                        {
                            var cells = new LayoutCell[table.Columns.Length];
                            int totalLines = 0;
                            for (int index = 0; index < cells.Length; index++)
                            {
                                var cell = index < row.Cells.Length ? row.Cells[index] : default;
                                if (cell.CheckMark is { } check)
                                    cells[index] = new LayoutCell([[]], check);
                                else
                                {
                                    var value = cell.Format(document.Culture, table.Columns[index].DisplayFormat);
                                    if (index == 0 && row.Kind == DocumentRowKind.Group)
                                        value = new string(' ', Math.Clamp(row.OutlineLevel, 0, 8) * 2) + value;
                                    cells[index] = new LayoutCell(wraps.Wrap(index, value, widths[index] - Padding * 2));
                                }
                                totalLines = Math.Max(totalLines, cells[index].Lines.Length);
                            }
                            int consumed = startConsumed;
                            startConsumed = 0;
                            while (consumed < totalLines)
                            {
                                int capacity = (int)((builder.Bottom - builder.Y - Padding * 2) / _measurer.LineHeight(BodySize));
                                if (capacity < 1)
                                {
                                    yield return builder.Finish(pageCount, _measurer);
                                    builder = NewPage(section, document, ++number, measureOnly);
                                    pageIndex?.Record(number, new PageCheckpoint(sectionIndex, blockIndex, rowIndex, consumed,
                                        table.RepeatHeader));
                                    if (table.RepeatHeader)
                                        DrawRow(builder, table.Columns, widths, header, 0, headerLines, header: true, DocumentRowKind.Data);
                                    capacity = (int)((builder.Bottom - builder.Y - Padding * 2) / _measurer.LineHeight(BodySize));
                                    if (capacity < 1)
                                        throw new InvalidOperationException("The page cannot fit a data row after its header.");
                                }

                                int slice = Math.Min(totalLines - consumed, capacity);
                                DrawRow(builder, table.Columns, widths, cells, consumed, slice, header: false, row.Kind);
                                consumed += slice;
                            }
                            rowIndex++;
                        }
                        builder.Y += Padding;
                        break;
                }
            }
            yield return builder.Finish(pageCount, _measurer);
        }
    }

    private static IAsyncEnumerable<DocumentRow> RowsFrom(DocumentTable table, long first, CancellationToken token) =>
        // Paginating from the start (count, print, PDF) reads ahead on another core; a resumed page reads directly.
        first <= 0 ? RowPrefetch.ReadAsync(table.OpenRows(token), token)
            : table.OpenRowsFrom?.Invoke(first, token) ?? SkipRows(table.OpenRows(token), first, token);

    private static async IAsyncEnumerable<DocumentRow> SkipRows(IAsyncEnumerable<DocumentRow> rows, long count,
        [EnumeratorCancellation] CancellationToken token)
    {
        long skipped = 0;
        await foreach (var row in rows.WithCancellation(token).ConfigureAwait(false))
        {
            if (skipped++ < count)
                continue;
            yield return row;
        }
    }

    private PageBuilder NewPage(DocumentSection section, Document document, int number, bool measureOnly)
    {
        var builder = new PageBuilder(section, number, document.Culture, measureOnly);
        if (!section.ShowHeading)
            return builder;
        string title = section.Header ?? document.Title;
        if (measureOnly)
        {
            // Advance by the same heading height as the drawn page, without shaping the text.
            builder.Y += (string.IsNullOrEmpty(title) ? 0 : 24) + 15;
            return builder;
        }
        if (!string.IsNullOrEmpty(title))
        {
            var run = _measurer.Shape(title, 16);
            builder.DrawText(run, section.Page.Left, builder.Y + 16, 16);
            builder.Y += 24;
        }
        var date = _measurer.Shape(document.CreatedAt.ToString("d", document.Culture), 8);
        builder.DrawText(date, section.Page.Left, builder.Y + 8, 8);
        builder.Y += 15;
        return builder;
    }

    private static double[] ColumnWidths(ImmutableArray<DocumentColumn> columns, PageSetup page, bool fit)
    {
        var widths = columns.Select(column => double.IsFinite(column.Width) ? Math.Clamp(column.Width, 12, 14400) : 100).ToArray();
        double total = widths.Sum();
        double scale = fit && total > page.ContentWidth ? page.ContentWidth / total : 1;
        for (int index = 0; index < widths.Length; index++)
            widths[index] *= scale;
        return widths;
    }

    private ImmutableArray<PositionedGlyph>[] Wrap(string text, double maxWidth)
    {
        maxWidth = Math.Max(1, maxWidth);
        var lines = new List<ImmutableArray<PositionedGlyph>>();
        foreach (var segment in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var glyphs = _measurer.Shape(segment, BodySize);
            int start = 0;
            if (glyphs.Length == 0)
                lines.Add([]);
            while (start < glyphs.Length)
            {
                int end = start;
                int space = -1;
                double width = 0;
                while (end < glyphs.Length && (end == start || width + glyphs[end].Advance <= maxWidth))
                {
                    if (glyphs[end].Unicode == ' ')
                        space = end;
                    width += glyphs[end].Advance;
                    end++;
                }
                if (end < glyphs.Length && space > start)
                    end = space + 1;
                lines.Add(glyphs[start..end]);
                start = end;
            }
        }
        return lines.ToArray();
    }

    private double RowHeight(int lines) => _measurer.LineHeight(BodySize) * lines + Padding * 2;

    private void DrawRow(PageBuilder page, ImmutableArray<DocumentColumn> columns, double[] widths,
        LayoutCell[] cells, int start, int lineCount, bool header, DocumentRowKind kind)
    {
        double x = page.Setup.Left;
        double height = RowHeight(lineCount);
        double y = page.Y;
        if (page.MeasureOnly)
        {
            page.Y += height;
            return;
        }
        var color = header ? new DocumentColor(237, 237, 244)
            : kind == DocumentRowKind.Data ? new DocumentColor(255, 255, 255) : new DocumentColor(243, 243, 250);
        // One fill and one rule per row keep large documents small. Text wider than its cell (a single glyph in a
        // very narrow column) needs the per-cell fills, which cover the overflow as the next cell is drawn.
        bool overflow = false;
        for (int index = 0; index < columns.Length && !overflow; index++)
        {
            var lines = cells[index].Lines;
            for (int line = 0; line < lineCount && start + line < lines.Length && !overflow; line++)
                overflow = Padding + Width(lines[start + line]) > widths[index] + 0.01;
        }
        if (!overflow)
        {
            double total = widths.Sum();
            page.Add(new FillRectangle(x, y, total, height, color));
            page.Add(new DrawLine(x, y + height, x + total, y + height, 0.5, DocumentColor.Outline));
        }
        for (int index = 0; index < columns.Length; index++)
        {
            if (overflow)
            {
                page.Add(new FillRectangle(x, y, widths[index], height, color));
                page.Add(new DrawLine(x, y + height, x + widths[index], y + height, 0.5, DocumentColor.Outline));
            }
            if (start == 0 && cells[index].CheckMark == true)
                DrawCheckMark(page, x, y + Padding + _measurer.LineHeight(BodySize) / 2, widths[index], columns[index].Alignment);
            var lines = cells[index].Lines;
            for (int line = 0; line < lineCount && start + line < lines.Length; line++)
            {
                var glyphs = lines[start + line];
                double glyphWidth = Width(glyphs);
                double left = columns[index].Alignment switch
                {
                    HorizontalAlignment.Right => Math.Max(x + Padding, x + widths[index] - Padding - glyphWidth),
                    HorizontalAlignment.Center => Math.Max(x + Padding, x + (widths[index] - glyphWidth) / 2),
                    _ => x + Padding
                };
                page.DrawText(glyphs, left, y + Padding + BodySize + line * _measurer.LineHeight(BodySize), BodySize);
            }
            x += widths[index];
        }
        page.Y += height;
    }

    private static double Width(ImmutableArray<PositionedGlyph> glyphs)
    {
        double width = 0;
        foreach (var glyph in glyphs)
            width += glyph.Advance;
        return width;
    }

    private static void DrawCheckMark(PageBuilder page, double x, double center, double columnWidth, HorizontalAlignment alignment)
    {
        var width = Math.Min(8, columnWidth - 2 * Padding);
        if (width < 2)
            return;
        double left = alignment switch
        {
            HorizontalAlignment.Right => x + columnWidth - Padding - width,
            HorizontalAlignment.Center => x + (columnWidth - width) / 2,
            _ => x + Padding
        };
        double bend = left + width * 0.35;
        page.Add(new DrawLine(left, center, bend, center + width * 0.3, 1, DocumentColor.Black));
        page.Add(new DrawLine(bend, center + width * 0.3, left + width, center - width * 0.35, 1, DocumentColor.Black));
    }

    private readonly record struct LayoutCell(ImmutableArray<PositionedGlyph>[] Lines, bool? CheckMark = null);

    /// <summary>Bounded per-column memo of wrapped cell text for one pagination pass.</summary>
    private sealed class WrapCache(Paginator owner)
    {
        private const int Capacity = 2048;
        private const int MaxLength = 256;
        private Dictionary<(string Text, double Width), ImmutableArray<PositionedGlyph>[]>?[] _columns = [];

        public void Reset(int columns) =>
            _columns = new Dictionary<(string Text, double Width), ImmutableArray<PositionedGlyph>[]>?[columns];

        public ImmutableArray<PositionedGlyph>[] Wrap(int column, string text, double width)
        {
            if (text.Length > MaxLength || (uint)column >= (uint)_columns.Length)
                return owner.Wrap(text, width);
            var cache = _columns[column] ??= new Dictionary<(string Text, double Width), ImmutableArray<PositionedGlyph>[]>();
            if (cache.TryGetValue((text, width), out var lines))
                return lines;
            lines = owner.Wrap(text, width);
            if (cache.Count < Capacity)
                cache[(text, width)] = lines;
            return lines;
        }
    }

    private sealed class PageBuilder(DocumentSection section, int number, CultureInfo culture, bool measureOnly)
    {
        public bool MeasureOnly => measureOnly;
        private readonly ImmutableArray<DrawCommand>.Builder _commands = ImmutableArray.CreateBuilder<DrawCommand>();
        public PageSetup Setup => section.Page;
        public double Y { get; set; } = section.Page.Top;
        public double Bottom => Setup.PageHeight - Setup.Bottom - FooterReserve;

        public bool CanFit(double height) => Y + height <= Bottom;
        public void Add(DrawCommand command)
        {
            if (!measureOnly)
                _commands.Add(command);
        }

        public void DrawText(ImmutableArray<PositionedGlyph> glyphs, double x, double baseline, double size)
        {
            if (!measureOnly && !glyphs.IsEmpty)
                _commands.Add(new DrawGlyphs(x, baseline, size, DocumentColor.Black, glyphs));
        }

        public DocumentPage Finish(int count, ITextMeasurer measurer)
        {
            if (measureOnly)
                return new DocumentPage(number, Setup, []);
            var footer = section.Footer ?? "";
            if (section.PageNumbers)
            {
                if (footer.Length > 0)
                    footer += "  •  ";
                footer += count == DeferredPageCount ? $"Page {number.ToString(culture)} of "
                    : count > 0 ? $"Page {number.ToString(culture)} of {count.ToString(culture)}"
                    : $"Page {number.ToString(culture)}";
            }
            if (footer.Length > 0)
            {
                var glyphs = measurer.Shape(footer, 8);
                double baseline = Setup.PageHeight - Setup.Bottom;
                DrawText(glyphs, Setup.Left, baseline, 8);
                // The total is not known yet: a writer fills it in once the last page is written.
                if (section.PageNumbers && count == DeferredPageCount)
                    _commands.Add(new DrawPageTotal(Setup.Left + glyphs.Sum(glyph => glyph.Advance), baseline, 8,
                        DocumentColor.Black));
            }
            return new DocumentPage(number, Setup, _commands.ToImmutable());
        }
    }
}
