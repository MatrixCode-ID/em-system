using System.IO;
using System.Runtime.CompilerServices;
using Em.Documents.Layout;
using Em.Documents.Model;
using Em.Documents.Pdf;
using Em.Controls.Write.Layout;
using Em.Controls.Write.Model;

namespace Em.Controls.Write.IO;

public static partial class WriteFiles
{
    /// <summary>Exports a document as PDF with the bundled font.</summary>
    /// <param name="document">The document.</param>
    /// <param name="destination">The stream to write.</param>
    /// <param name="cancellation">Cancels the export.</param>
    /// <returns>A task that completes when the PDF is written.</returns>
    public static ValueTask SavePdfAsync(WriteDocument document, Stream destination, CancellationToken cancellation = default) =>
        SavePdfAsync(document, destination, null, cancellation);

    /// <summary>Exports PDF with the faces <paramref name="fonts"/> resolves, embedding a subset of each face used.</summary>
    public static ValueTask SavePdfAsync(WriteDocument document, Stream destination, global::Em.Editing.IFontProvider? fonts,
        CancellationToken cancellation = default)
    {
        var writer = new PdfDocumentWriter();
        return writer.WritePagesAsync("Document", System.Globalization.CultureInfo.InvariantCulture,
            PdfPages(document, fonts, cancellation), destination, cancellationToken: cancellation);
    }

    private static async IAsyncEnumerable<DocumentPage> PdfPages(WriteDocument document, global::Em.Editing.IFontProvider? fonts,
        [EnumeratorCancellation] CancellationToken cancellation)
    {
        await Task.CompletedTask;
        var paginator = new WritePaginator(fonts);
        int count = paginator.Pages(document, cancellation: cancellation).Count();
        const double point = .75;
        foreach (var page in paginator.Pages(document, count, cancellation))
        {
            var setup = new PageSetup(page.Settings.Width * point, page.Settings.Height * point,
                Orientation: page.Settings.Width > page.Settings.Height ? PageOrientation.Landscape : PageOrientation.Portrait);
            yield return new(page.Number, setup, [.. page.Commands.Select(command => command switch
            {
                DrawGlyphs text => text with { X = text.X * point, Baseline = text.Baseline * point, Size = text.Size * point,
                    Glyphs = [.. text.Glyphs.Select(glyph => glyph with { Advance = glyph.Advance * point })] },
                DrawLine line => line with { X1 = line.X1 * point, Y1 = line.Y1 * point, X2 = line.X2 * point,
                    Y2 = line.Y2 * point, Width = line.Width * point },
                FillRectangle fill => fill with { X = fill.X * point, Y = fill.Y * point, Width = fill.Width * point, Height = fill.Height * point },
                _ => command
            })]);
        }
    }
}
