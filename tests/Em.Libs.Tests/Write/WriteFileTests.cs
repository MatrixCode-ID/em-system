using System.IO;
using System.IO.Compression;
using System.Text;
using Em.Write.Editing;
using Em.Write.IO;
using Em.Write.Model;
using Xunit;

namespace Em.Libs.Tests.Write;

public sealed class WriteFileTests
{
    [Fact]
    public void Docx_round_trip_keeps_styles_lists_images_tables_and_headers()
    {
        var session = new WriteSession();
        session.InsertText("First");
        session.SelectAll();
        session.ApplyStyle("Heading1", WriteStyleKind.Paragraph);
        session.SetList(WriteListKind.Decimal);
        var selected = session.Selection.Caret;
        session.Select(new(selected, selected));
        session.InsertRun(new("", Image: new([1, 2, 3], "image/png", 60, 80)));
        session.InsertTable(2, 2);
        session.SetHeaderRepeat(true);
        session.InsertText("Table");
        session.EditStory(WriteStory.FirstHeader, 0);
        session.SetPage(new(DifferentFirstPage: true));
        session.InsertText("Title");
        session.InsertRun(new("", Field: WriteField.PageCount));

        using var stream = new MemoryStream();
        WriteFiles.SaveDocx(session.Document, stream);
        stream.Position = 0;
        var result = WriteFiles.OpenDocx(stream);
        Assert.Empty(result.Warnings);
        var section = Assert.Single(result.Document.Sections);
        Assert.True(section.Page.DifferentFirstPage);
        var first = Assert.IsType<WriteParagraph>(section.Blocks[0]);
        Assert.Equal("Heading1", first.StyleId);
        Assert.NotNull(first.NumberingId);
        var image = first.Runs.Single(run => run.Image is not null).Image!;
        Assert.Equal(60, image.Width);
        Assert.Equal(new byte[] { 1, 2, 3 }, image.Bytes.ToArray());
        var table = Assert.IsType<WriteTable>(section.Blocks[1]);
        Assert.True(table.Rows[0].RepeatHeader);
        Assert.Equal("Table", Assert.IsType<WriteParagraph>(Assert.Single(table.Rows[0].Cells[0].Blocks)).Text);
        var header = Assert.IsType<WriteParagraph>(Assert.Single(section.Stories[WriteStory.FirstHeader]));
        Assert.Equal(WriteField.PageCount, header.Runs[^1].Field);
    }

    [Fact]
    public async Task Pdf_uses_write_page_geometry_and_unicode_mapping()
    {
        var session = new WriteSession();
        session.InsertText("Olá");
        session.InsertBreak(false);
        session.InsertText("Second");
        using var stream = new MemoryStream();
        await WriteFiles.SavePdfAsync(session.Document, stream);
        var pdf = Encoding.Latin1.GetString(stream.ToArray());
        Assert.StartsWith("%PDF-", pdf);
        Assert.Contains("/Type /Pages /Count 2", pdf);
        Assert.Contains("/MediaBox [0 0 612 792]", pdf);
        Assert.Contains("/ToUnicode", pdf);
        int map = pdf.IndexOf("\n8 0 obj\n", StringComparison.Ordinal);
        Assert.True(map > 0);
        int start = pdf.IndexOf("stream\n", map, StringComparison.Ordinal) + 7;
        int end = pdf.IndexOf("\nendstream", start, StringComparison.Ordinal);
        using var compressed = new MemoryStream(stream.ToArray(), start, end - start);
        using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
        using var text = new StreamReader(inflater);
        Assert.Contains("<00E1>", await text.ReadToEndAsync());
    }
}
