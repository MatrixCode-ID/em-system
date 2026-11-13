// Ported Unicode CID mapping and content serialization from PdfCraft (crates/fonts/src/embedding.rs,
// crates/content/src/lib.rs @ 059dbd6e00507cdfedd755bf6b712f883507fce0), MIT OR Apache-2.0.
// Copyright (c) 2026 ArtCraft Team and the PdfCraft contributors.
using System.Collections.Concurrent;
using System.IO;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Em.Documents.Fonts;
using Em.Documents.Formats;
using Em.Documents.Layout;
using Em.Documents.Model;

namespace Em.Documents.Pdf;

/// <summary>Writes documents as PDF files with an embedded subset font.</summary>
public sealed class PdfDocumentWriter : IDocumentWriter
{
    private readonly TrueTypeFace _face;
    private readonly Paginator _paginator;

    /// <summary>Creates a PDF writer.</summary>
    /// <param name="face">The font to embed, or null for the bundled font.</param>
    public PdfDocumentWriter(TrueTypeFace? face = null)
    {
        _face = face ?? TrueTypeFace.Default;
        _paginator = new Paginator(new TrueTypeMeasurer(_face));
    }

    /// <inheritdoc/>
    public DocumentFormat Format { get; } = new("pdf", "PDF document", ".pdf", "application/pdf", FormatCapabilities.Paged);

    /// <inheritdoc/>
    public ValueTask WriteAsync(Document document, Stream destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);
        if (document.Sections.IsDefaultOrEmpty)
            throw new ArgumentException("A PDF document needs at least one section.", nameof(document));

        return WritePagesAsync(document.Title, document.Culture,
            _paginator.PagesAsync(document, Paginator.DeferredPageCount, cancellationToken), destination,
            document.PageProgress, cancellationToken);
    }

    internal async ValueTask WritePagesAsync(string title, CultureInfo culture, IAsyncEnumerable<DocumentPage> pages,
        Stream destination, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        // Single pass: pages are written as they are laid out. Character codes are assigned on first use, and the
        // fonts, page tree and page total are written after the last page, which PDF allows in any object order.
        // The default face uses objects 3-8; another face gets six object numbers when a page first uses it.
        const int Catalog = 1, PageTree = 2, Font = 3, Info = 9, PageTotal = 10, FirstPage = 11;
        var faces = new Dictionary<TrueTypeFace, PdfFace>(ReferenceEqualityComparer.Instance)
        {
            [_face] = new(_face, 0, Font)
        };
        var pdf = new PdfSyntaxWriter(destination, FirstPage + 1);
        int written = 0, flushed = 0, next = FirstPage;
        var pageIds = new List<int>();
        // Layout stays sequential; each page's content stream is serialized and compressed on a worker, and the
        // finished pages are written in order.
        var pending = new Queue<(int PageId, string Dictionary, Task<byte[]> Content)>();
        int parallel = Math.Clamp(Environment.ProcessorCount - 2, 1, 8);
        async Task WriteNextAsync()
        {
            var (pageId, dictionary, content) = pending.Dequeue();
            pdf.Object(pageId, dictionary);
            pdf.CompressedStream(pageId + 1, await content.ConfigureAwait(false));
            if ((++flushed & 15) == 0)
                progress?.Report(flushed);
        }
        try
        {
            await foreach (var page in pages.ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int pageId = next;
                next += 2;
                written++;
                pageIds.Add(pageId);
                var used = new SortedSet<int> { 0 };
                foreach (var command in page.Commands)
                    if (command is DrawGlyphs text)
                    {
                        var face = Face(faces, text.Face ?? _face, ref next);
                        used.Add(face.Index);
                        foreach (var glyph in text.Glyphs)
                            Code(face.Codes, glyph);
                    }
                var fonts = new StringBuilder();
                foreach (var face in faces.Values.Where(face => used.Contains(face.Index)).OrderBy(face => face.Index))
                    fonts.Append(CultureInfo.InvariantCulture, $" /F{face.Index} {face.Object} 0 R");
                var dictionary = FormattableString.Invariant($"<< /Type /Page /Parent {PageTree} 0 R /MediaBox [0 0 {Number(page.Setup.PageWidth)} {Number(page.Setup.PageHeight)}] /Resources << /Font <<{fonts} >> /XObject << /Total {PageTotal} 0 R >> >> /Contents {pageId + 1} 0 R >>");
                var laidOut = page;
                var lookup = new Dictionary<TrueTypeFace, PdfFace>(faces);
                pending.Enqueue((pageId, dictionary, Task.Run(() => PdfSyntaxWriter.Deflate(Serialize(laidOut, lookup, _face)),
                    cancellationToken)));
                while (pending.Count > parallel)
                    await WriteNextAsync().ConfigureAwait(false);
            }
            while (pending.Count > 0)
                await WriteNextAsync().ConfigureAwait(false);
        }
        finally
        {
            // Let abandoned workers finish before the destination is closed.
            foreach (var item in pending)
                try { await item.Content.ConfigureAwait(false); } catch (Exception) { }
        }
        progress?.Report(written);
        if (written < 1)
            throw new ArgumentException("A PDF document needs at least one page.", nameof(pages));

        // The page total is drawn by every footer through one form object.
        var defaultFace = faces[_face];
        var totalGlyphs = new TrueTypeMeasurer(_face).Shape(written.ToString(culture), 8);
        var total = new StringBuilder("BT /F0 8 Tf ").Append(Color(DocumentColor.Black)).Append(" rg 0 0 Td <");
        foreach (var glyph in totalGlyphs)
            AppendHex(total, Code(defaultFace.Codes, glyph));
        total.Append("> Tj ET\n");
        pdf.Stream(PageTotal, Encoding.ASCII.GetBytes(total.ToString()), FormattableString.Invariant(
            $"/Type /XObject /Subtype /Form /BBox [0 -4 200 12] /Resources << /Font << /F0 {Font} 0 R >> >>"));

        foreach (var face in faces.Values)
            WriteFont(pdf, face);
        pdf.Object(Catalog, $"<< /Type /Catalog /Pages {PageTree} 0 R >>");
        var children = string.Join(" ", pageIds.Select(id => id.ToString(CultureInfo.InvariantCulture) + " 0 R"));
        pdf.Object(PageTree, FormattableString.Invariant($"<< /Type /Pages /Count {written} /Kids [{children}] >>"));
        string encodedTitle = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(title));
        pdf.Object(Info, $"<< /Title <FEFF{encodedTitle}> /Producer (MatrixGrid) >>");
        pdf.Finish(Catalog, Info);
    }

    private static PdfFace Face(Dictionary<TrueTypeFace, PdfFace> faces, TrueTypeFace face, ref int next)
    {
        if (faces.TryGetValue(face, out var known))
            return known;
        var added = new PdfFace(face, faces.Count, next);
        next += 6;
        faces.Add(face, added);
        return added;
    }

    // Objects of one face: the Type0 font, its CID font, descriptor, font file, CID-to-glyph map and ToUnicode map.
    private static void WriteFont(PdfSyntaxWriter pdf, PdfFace entry)
    {
        var face = entry.Face;
        var codes = entry.Codes;
        if (codes.Count >= ushort.MaxValue)
            throw new InvalidOperationException("The PDF uses more characters than the embedded font map supports.");
        var ordered = codes.Values.OrderBy(character => character.Code).ToArray();
        var font = face.Subset(ordered.Select(character => character.Glyph));
        var cidToGlyph = new byte[(ordered.Length + 1) * 2];
        foreach (var code in ordered)
            BinaryPrimitives.WriteUInt16BigEndian(cidToGlyph.AsSpan(code.Code * 2), code.Glyph);
        int id = entry.Object;
        string Metric(int value) => Number(1000.0 * value / face.UnitsPerEm);
        var fontName = (face.CanSubset ? Tag(entry.Index) + "+" : "") + face.PostScriptName;
        pdf.Object(id, $"<< /Type /Font /Subtype /Type0 /BaseFont /{fontName} /Encoding /Identity-H /DescendantFonts [{id + 1} 0 R] /ToUnicode {id + 5} 0 R >>");
        var widths = string.Join(" ", ordered.Select(code => Metric(face.Advance(code.Glyph))));
        pdf.Object(id + 1, $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{fontName} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {id + 2} 0 R /CIDToGIDMap {id + 4} 0 R /W [1 [{widths}]] >>");
        var box = face.Bounds;
        pdf.Object(id + 2, $"<< /Type /FontDescriptor /FontName /{fontName} /Flags 32 /FontBBox [{Metric(box.Left)} {Metric(box.Bottom)} {Metric(box.Right)} {Metric(box.Top)}] /ItalicAngle 0 /Ascent {Metric(face.Ascent)} /Descent {Metric(face.Descent)} /CapHeight {Metric(face.Ascent)} /StemV 80 /FontFile2 {id + 3} 0 R >>");
        pdf.Stream(id + 3, font, FormattableString.Invariant($"/Length1 {font.Length}"));
        pdf.Stream(id + 4, cidToGlyph);
        pdf.Stream(id + 5, Encoding.ASCII.GetBytes(ToUnicode(ordered)));
    }

    // A six-letter subset tag per face: MATRIX for the default face, then MATRIB, MATRIC, ...
    private static string Tag(int index) => index == 0 ? "MATRIX" : "MATR" + (char)('A' + index / 26 % 26) + (char)('A' + index % 26);

    private static ushort Code(ConcurrentDictionary<int, Character> codes, PositionedGlyph glyph)
    {
        if (codes.TryGetValue(glyph.Unicode, out var known))
            return known.Code;
        if (codes.Count >= ushort.MaxValue - 1)
            throw new InvalidOperationException("The PDF uses more characters than the embedded font map supports.");
        var added = new Character(glyph.Unicode, glyph.Id, (ushort)(codes.Count + 1));
        codes.TryAdd(glyph.Unicode, added);
        return added.Code;
    }

    private static byte[] Serialize(DocumentPage page, IReadOnlyDictionary<TrueTypeFace, PdfFace> faces, TrueTypeFace fallback)
    {
        double height = page.Setup.PageHeight;
        var content = new StringBuilder("q\n");
        foreach (var item in page.Commands)
        {
            switch (item)
            {
                case FillRectangle rect:
                    content.Append(Color(rect.Color)).Append(" rg ");
                    AppendNumber(content, rect.X).Append(' ');
                    AppendNumber(content, height - rect.Y - rect.Height).Append(' ');
                    AppendNumber(content, rect.Width).Append(' ');
                    AppendNumber(content, rect.Height).Append(" re f\n");
                    break;
                case DrawLine line:
                    content.Append(Color(line.Color)).Append(" RG ");
                    AppendNumber(content, line.Width).Append(" w ");
                    AppendNumber(content, line.X1).Append(' ');
                    AppendNumber(content, height - line.Y1).Append(" m ");
                    AppendNumber(content, line.X2).Append(' ');
                    AppendNumber(content, height - line.Y2).Append(" l S\n");
                    break;
                case DrawGlyphs text:
                    var face = faces[text.Face ?? fallback];
                    content.Append("BT /F").Append(face.Index).Append(' ');
                    AppendNumber(content, text.Size).Append(" Tf ").Append(Color(text.Color)).Append(" rg ");
                    if (text.SyntheticBold)
                    {
                        // Fill and stroke the outlines to embolden a face that has no bold style.
                        content.Append(Color(text.Color)).Append(" RG 2 Tr ");
                        AppendNumber(content, text.Size * .03).Append(" w ");
                    }
                    // A slant of about 12 degrees stands in for a missing italic style.
                    content.Append(text.SyntheticItalic ? "1 0 0.21 1 " : "1 0 0 1 ");
                    AppendNumber(content, text.X).Append(' ');
                    AppendNumber(content, height - text.Baseline).Append(" Tm <");
                    foreach (var glyph in text.Glyphs)
                        AppendHex(content, face.Codes[glyph.Unicode].Code);
                    content.Append("> Tj ET\n");
                    break;
                case DrawPageTotal total:
                    content.Append("q 1 0 0 1 ");
                    AppendNumber(content, total.X).Append(' ');
                    AppendNumber(content, height - total.Baseline).Append(" cm /Total Do Q\n");
                    break;
            }
        }
        // Content streams are ASCII: operators, numbers and hex glyph codes.
        return Encoding.ASCII.GetBytes(content.Append("Q\n").ToString());
    }

    private static void AppendHex(StringBuilder content, ushort code)
    {
        const string Digits = "0123456789ABCDEF";
        content.Append(Digits[code >> 12]).Append(Digits[(code >> 8) & 15]).Append(Digits[(code >> 4) & 15])
            .Append(Digits[code & 15]);
    }

    /// <summary>Appends a number rounded to four decimals without trailing zeros, like the "0.####" format.</summary>
    private static StringBuilder AppendNumber(StringBuilder content, double value)
    {
        double rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) < 1e15 && rounded == Math.Truncate(rounded))
            return content.Append((long)rounded);
        Span<char> buffer = stackalloc char[40];
        if (!rounded.TryFormat(buffer, out int length, "F4", CultureInfo.InvariantCulture))
            return content.Append(rounded.ToString("0.####", CultureInfo.InvariantCulture));
        while (length > 0 && buffer[length - 1] == '0')
            length--;
        if (length > 0 && buffer[length - 1] == '.')
            length--;
        return content.Append(buffer[..length]);
    }

    private static string ToUnicode(IEnumerable<Character> codes)
    {
        var text = new StringBuilder("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
            .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
            .Append("/CMapName /MatrixGridUnicode def\n/CMapType 2 def\n")
            .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var chunk in codes.Chunk(100))
        {
            text.Append(chunk.Length.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            foreach (var code in chunk)
            {
                string scalar = new Rune(code.Unicode).ToString();
                text.Append('<').Append(code.Code.ToString("X4", CultureInfo.InvariantCulture)).Append("> <")
                    .Append(Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(scalar))).Append(">\n");
            }
            text.Append("endbfchar\n");
        }
        return text.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n").ToString();
    }

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static readonly ConcurrentDictionary<DocumentColor, string> Colors = new();

    private static string Color(DocumentColor color) => Colors.GetOrAdd(color, static value =>
        $"{Number(value.Red / 255.0)} {Number(value.Green / 255.0)} {Number(value.Blue / 255.0)}");

    private readonly record struct Character(int Unicode, ushort Glyph, ushort Code);

    // Codes are assigned in page order on the writer's thread, so the output is deterministic; page workers only read them.
    private sealed record PdfFace(TrueTypeFace Face, int Index, int Object)
    {
        public ConcurrentDictionary<int, Character> Codes { get; } = new();
    }
}
