// Ported from WordCraft (layout/src/flow.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using System.Collections.Immutable;
using System.Globalization;
using Em.Documents.Layout;
using Em.Editing;
using Em.Write.Editing;
using Em.Write.Model;

namespace Em.Write.Layout;

/// <summary>Lays out word-processing documents into pages.</summary>
/// <param name="fonts">The font provider used to measure text, or null for the bundled font.</param>
public sealed partial class WritePaginator(IFontProvider? fonts = null)
{
    private readonly IFontProvider fonts = fonts ?? BundledFontProvider.Instance;

    /// <summary>Lays out the pages of a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="totalPages">The page total printed by page count fields, or null when it is not known.</param>
    /// <param name="cancellation">Cancels the layout.</param>
    /// <returns>The pages in order.</returns>
    public IEnumerable<WritePage> Pages(WriteDocument document, int? totalPages = null, CancellationToken cancellation = default)
    {
        int pageNumber = 0;
        var numbering = new Dictionary<int, int>();
        for (int sectionIndex = 0; sectionIndex < document.Sections.Length; sectionIndex++)
        {
            var section = document.Sections[sectionIndex];
            var settings = section.Page;
            double bottom = Math.Max(settings.Top + 1, settings.Height - settings.Bottom);
            double width = Math.Max(1, settings.Width - settings.Left - settings.Right);
            int first = pageNumber + 1;
            var page = NewPage();
            foreach (var (block, blockIndex) in section.Blocks.Select((block, index) => (block, index)))
            {
                cancellation.ThrowIfCancellationRequested();
                var position = new WritePosition(sectionIndex, blockIndex, 0);
                if (block is WritePageBreak)
                {
                    yield return page.Finish();
                    page = NewPage();
                    continue;
                }
                if (block is WriteTable table)
                {
                    var rowIndex = 0;
                    var headers = table.Rows.TakeWhile(row => row.RepeatHeader).Count();
                    foreach (var row in table.Rows)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var lines = MeasureRow(document, table, row, position with { Row = rowIndex }, settings.Left, width,
                            page.Number, totalPages);
                        int at = 0;
                        while (at < lines.Count)
                        {
                            if (page.Y + lines[at].Height > bottom && page.Y > settings.Top)
                            {
                                yield return page.Finish();
                                page = NewPage();
                                if (rowIndex >= headers)
                                    for (int h = 0; h < headers; h++)
                                    foreach (var repeat in MeasureRow(document, table, table.Rows[h], position with { Row = h },
                                        settings.Left, width, page.Number, totalPages))
                                    {
                                        if (page.Y + repeat.Height + lines[at].Height <= bottom)
                                            PlaceRow(page, repeat, totalPages);
                                    }
                            }
                            PlaceRow(page, lines[at++], totalPages);
                        }
                        rowIndex++;
                    }
                    continue;
                }
                if (block is not WriteParagraph paragraph)
                    continue;
                var paragraphStyle = document.Resolve(paragraph).Paragraph;
                string? label = null;
                if (paragraph.NumberingId is { } id && document.Numbering.TryGetValue(id, out var definition))
                {
                    int index = numbering.GetValueOrDefault(id);
                    label = definition.Format(index);
                    numbering[id] = index + 1;
                }
                var measured = MeasureParagraph(document, paragraph, position, width, page.Number, totalPages, label);
                double before = Math.Max(0, paragraphStyle.SpaceBefore ?? 0), after = Math.Max(0, paragraphStyle.SpaceAfter ?? 0);
                double keep = paragraphStyle.KeepWithNext == true && blockIndex + 1 < section.Blocks.Length &&
                    section.Blocks[blockIndex + 1] is WriteParagraph next ?
                    MeasureParagraph(document, next, position with { Block = blockIndex + 1 }, width, page.Number, totalPages, null)[0].Height : 0;
                if (page.Y > settings.Top && (paragraphStyle.PageBreakBefore == true ||
                    keep > 0 && page.Y + before + measured.Sum(line => line.Height) + after + keep > bottom))
                {
                    yield return page.Finish();
                    page = NewPage();
                }
                page.Y += before;
                for (int i = 0; i < measured.Count;)
                {
                    cancellation.ThrowIfCancellationRequested();
                    double used = 0;
                    int fits = 0;
                    while (i + fits < measured.Count && page.Y + used + measured[i + fits].Height <= bottom)
                        used += measured[i + fits++].Height;
                    bool widow = paragraphStyle.WidowControl != false && measured.Count >= 3;
                    if (widow && fits > 2 && i + fits == measured.Count - 1)
                        fits--;
                    if ((fits == 0 || widow && fits == 1 && i + fits < measured.Count) && page.Y > settings.Top + before)
                    {
                        yield return page.Finish();
                        page = NewPage();
                        continue;
                    }
                    fits = Math.Max(1, fits);
                    for (int j = 0; j < fits; j++)
                    {
                        var line = measured[i++];
                        PlaceLine(page, line, settings.Left, page.Y, page.Number, totalPages);
                        page.Y += line.Height;
                    }
                }
                page.Y += after;
            }
            yield return page.Finish();

            PageBuilder NewPage()
            {
                var result = new PageBuilder(++pageNumber, sectionIndex, settings);
                foreach (bool header in new[] { true, false })
                {
                    WriteStory normal = header ? WriteStory.Header : WriteStory.Footer;
                    WriteStory variant = settings.DifferentFirstPage && pageNumber == first ?
                        header ? WriteStory.FirstHeader : WriteStory.FirstFooter :
                        settings.DifferentEvenPages && pageNumber % 2 == 0 ?
                        header ? WriteStory.EvenHeader : WriteStory.EvenFooter : normal;
                    int sourceSection = sectionIndex;
                    ImmutableArray<WriteBlock> story = [];
                    while (sourceSection >= 0)
                    {
                        var available = document.Sections[sourceSection].Stories;
                        if (available.TryGetValue(variant, out story))
                            break;
                        if (available.TryGetValue(normal, out story))
                        {
                            variant = normal;
                            break;
                        }
                        sourceSection--;
                    }
                    double y = header ? settings.HeaderDistance : Math.Max(0, settings.Height - settings.FooterDistance);
                    for (int i = 0; !story.IsDefault && i < story.Length; i++)
                        if (story[i] is WriteParagraph para)
                            foreach (var line in MeasureParagraph(document, para, new(sourceSection, i, 0, variant), width,
                                pageNumber, totalPages, null))
                            {
                                PlaceLine(result, line, settings.Left, y, pageNumber, totalPages);
                                y += line.Height;
                            }
                }
                return result;
            }
        }
    }

    private sealed class PageBuilder(int number, int section, WritePageSettings page)
    {
        internal int Number => number;
        internal double Y { get; set; } = page.Top;
        internal ImmutableArray<DrawCommand>.Builder Draw { get; } = ImmutableArray.CreateBuilder<DrawCommand>();
        internal ImmutableArray<WriteLine>.Builder Lines { get; } = ImmutableArray.CreateBuilder<WriteLine>();
        internal ImmutableArray<WritePlacedImage>.Builder Images { get; } = ImmutableArray.CreateBuilder<WritePlacedImage>();
        internal WritePage Finish() => new(number, section, page, Draw.ToImmutable(), Lines.ToImmutable(), Images.ToImmutable());
    }
}
