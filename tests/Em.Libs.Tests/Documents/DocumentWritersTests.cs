using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Em.Documents.Xlsx;
using Em.Documents.Fonts;
using Em.Documents.Layout;
using Em.Documents.Model;
using Em.Documents.Pdf;
using Em.Documents.Docx;
using Xunit;

namespace Em.Libs.Tests.Documents;

public sealed class DocumentWritersTests
{
    [Fact]
    public async Task Spreadsheet_has_typed_cells_frozen_header_and_outline()
    {
        using var stream = new MemoryStream();
        await new XlsxDocumentWriter().WriteAsync(Sample(), stream);
        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var xml = Read(package, "xl/worksheets/sheet1.xml");
        Assert.Equal("frozen", (string?)Assert.Single(xml.Descendants(sheet + "pane")).Attribute("state"));
        var rows = xml.Descendants(sheet + "row").ToArray();
        Assert.Equal("2", (string?)rows[1].Attribute("outlineLevel"));
        var cells = rows[1].Elements(sheet + "c").ToArray();
        Assert.Equal("12.50", cells[1].Element(sheet + "v")!.Value);
        Assert.Equal("61", cells[2].Element(sheet + "v")!.Value);
        Assert.Equal("b", (string?)cells[3].Attribute("t"));
        Assert.Equal("1", cells[3].Element(sheet + "v")!.Value);
        Assert.Equal(1, XlsxDocumentWriter.DateSerial(new DateOnly(1900, 1, 1)));
        Assert.Equal(59, XlsxDocumentWriter.DateSerial(new DateOnly(1900, 2, 28)));
        Assert.NotNull(package.GetEntry("_rels/.rels"));
        Assert.NotNull(package.GetEntry("[Content_Types].xml"));
        Assert.NotNull(package.GetEntry("xl/styles.xml"));
    }

    [Fact]
    public async Task Word_document_repeats_table_headers_and_uses_page_settings_and_fields()
    {
        using var stream = new MemoryStream();
        await new DocxDocumentWriter().WriteAsync(Sample(), stream);
        stream.Position = 0;
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var xml = Read(package, "word/document.xml");
        Assert.Single(xml.Descendants(word + "tblHeader"));
        Assert.Equal("landscape", (string?)Assert.Single(xml.Descendants(word + "pgSz")).Attribute(word + "orient"));
        Assert.Contains(xml.Descendants(word + "t"), text => text.Value.Contains("Café", StringComparison.Ordinal));
        var footer = Read(package, "word/footer1.xml");
        Assert.Equal(["PAGE", "NUMPAGES"], footer.Descendants(word + "fldSimple")
            .Select(node => node.Attribute(word + "instr")!.Value.Trim()));
    }

    [Fact]
    public async Task Csv_quotes_delimiters_quotes_and_line_breaks()
    {
        using var stream = new MemoryStream();
        await new CsvDocumentWriter(';').WriteAsync(Sample(), stream);
        var csv = Encoding.UTF8.GetString(stream.ToArray());
        Assert.StartsWith("Name;Amount;Date;Enabled\r\n", csv);
        Assert.Contains("\"Café; \"\"Blue\"\"\nSecond line\";12.50", csv);
    }

    [Fact]
    public async Task Pdf_has_shared_positions_subset_font_and_unicode_map()
    {
        var document = Sample(24);
        var paginator = new Paginator();
        var plan = await paginator.PlanAsync(document);
        Assert.True(plan.PageCount > 1);
        Assert.True(plan.Characters['é'] > 0);
        Assert.Contains('Ω', plan.Characters.Keys.Select(key => (char)key));
        var pages = new List<DocumentPage>();
        await foreach (var page in paginator.PagesAsync(document, plan.PageCount))
            pages.Add(page);
        Assert.Equal(plan.PageCount, pages.Count);
        Assert.All(pages, page => Assert.Contains(page.Commands.OfType<DrawGlyphs>(),
            run => string.Concat(run.Glyphs.Select(glyph => new Rune(glyph.Unicode).ToString())) == "Name"));

        using var stream = new MemoryStream();
        await new PdfDocumentWriter().WriteAsync(document, stream);
        var bytes = stream.ToArray();
        var pdf = Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-1.7", pdf);
        Assert.Contains($"/Count {plan.PageCount}", pdf);
        Assert.Contains("/FontFile2 6 0 R", pdf);
        var unicodeMap = Encoding.ASCII.GetString(DecodeObject(bytes, 8));
        Assert.Contains("<00E9>", unicodeMap);
        Assert.Contains("<03A9>", unicodeMap);
        var first = pages[0].Commands.OfType<DrawGlyphs>().First();
        var contents = Encoding.ASCII.GetString(DecodeObject(bytes, 11));
        string coordinate = string.Create(CultureInfo.InvariantCulture,
            $"1 0 0 1 {first.X:0.####} {pages[0].Setup.PageHeight - first.Baseline:0.####} Tm");
        Assert.Contains(coordinate, contents);

        var subset = DecodeObject(bytes, 6);
        using var full = typeof(TrueTypeFace).Assembly.GetManifestResourceStream(TrueTypeFace.DefaultResource)!;
        Assert.True(subset.Length < full.Length);
        var parsed = new TrueTypeFace(subset, "NotoSans-Regular");
        Assert.Equal(TrueTypeFace.Default.Glyph(new Rune('é')), parsed.Glyph(new Rune('é')));

        int start = pdf.LastIndexOf("startxref\n", StringComparison.Ordinal) + "startxref\n".Length;
        int offset = int.Parse(pdf[start..pdf.IndexOf('\n', start)], CultureInfo.InvariantCulture);
        Assert.Equal("xref", pdf.Substring(offset, 4));
    }

    private static XDocument Read(ZipArchive package, string part)
    {
        using var stream = package.GetEntry(part)!.Open();
        return XDocument.Load(stream);
    }

    private static byte[] DecodeObject(byte[] data, int id)
    {
        string bytes = Encoding.Latin1.GetString(data);
        int start = bytes.IndexOf($"\n{id} 0 obj\n", StringComparison.Ordinal);
        int content = bytes.IndexOf("\nstream\n", start, StringComparison.Ordinal) + "\nstream\n".Length;
        int lengthStart = bytes.IndexOf("/Length ", start, StringComparison.Ordinal) + "/Length ".Length;
        int length = int.Parse(bytes[lengthStart..bytes.IndexOf(' ', lengthStart)], CultureInfo.InvariantCulture);
        using var compressed = new MemoryStream(data, content, length, writable: false);
        using var decoder = new ZLibStream(compressed, CompressionMode.Decompress);
        using var decoded = new MemoryStream();
        decoder.CopyTo(decoded);
        return decoded.ToArray();
    }

    private static Document Sample(int count = 1) => new("Orders Ω", [
        new DocumentSection(new PageSetup(Width: 280, Height: 340, Left: 20, Top: 18, Right: 20, Bottom: 18,
                Orientation: PageOrientation.Landscape),
            [new DocumentTable([
                new DocumentColumn("Name", 110), new DocumentColumn("Amount", 64, HorizontalAlignment.Right, "0.00", "0.00"),
                new DocumentColumn("Date", 70), new DocumentColumn("Enabled", 60)
            ], token => Rows(count, token))])
    ]);

    private static async IAsyncEnumerable<DocumentRow> Rows(int count, [EnumeratorCancellation] CancellationToken token)
    {
        await Task.Yield();
        for (int i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested();
            yield return new DocumentRow([
                DocumentCell.Text("Café; \"Blue\"\nSecond line"), DocumentCell.Number(12.50m),
                DocumentCell.Date(new DateOnly(1900, 3, 1)), DocumentCell.Boolean(true)
            ], OutlineLevel: 2);
        }
    }
}
