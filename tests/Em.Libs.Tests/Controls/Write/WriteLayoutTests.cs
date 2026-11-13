using Em.Documents.Layout;
using Em.Controls.Write.Editing;
using Em.Controls.Write.Layout;
using Em.Controls.Write.Model;
using Xunit;

namespace Em.Libs.Tests.Controls.Write;

public sealed class WriteLayoutTests
{
    [Fact]
    public void Explicit_breaks_and_page_fields_are_laid_out()
    {
        var session = new WriteSession();
        session.InsertText("Page one");
        session.InsertBreak(false);
        session.InsertText("Page two");
        session.EditStory(WriteStory.Footer, 0);
        session.InsertText("Page ");
        session.InsertRun(new("", Field: WriteField.Page));
        session.InsertText(" of ");
        session.InsertRun(new("", Field: WriteField.PageCount));
        var pages = new WritePaginator().Pages(session.Document, 2).ToArray();
        Assert.Equal(2, pages.Length);
        Assert.Contains("Page 2 of 2", Text(pages[1]));
    }

    [Fact]
    public void Points_round_trip_to_caret_and_selection_geometry()
    {
        var page = new WritePaginator().Pages(WriteDocument.Create("An example")).Single();
        var caret = new WritePosition(0, 0, 3);
        var point = page.FindCaret(caret)!.Value;
        Assert.Equal(caret, page.HitTest(point.X, point.Top + point.Height / 2));
        Assert.Single(page.SelectionBounds(new(new(0, 0, 0), caret)));
    }

    [Fact]
    public void Table_spans_pages_and_repeats_header()
    {
        var session = new WriteSession();
        session.SetPage(new(320, 180, 20, 20, 20, 20));
        session.InsertTable(12, 2);
        session.SetHeaderRepeat(true);
        session.InsertText("Column heading");
        for (int i = 1; i < 12; i++)
        {
            var pos = new WritePosition(0, 1, 0, Row: i, Cell: 0);
            Assert.True(session.Select(new(pos, pos)));
            session.InsertText($"Item {i}");
        }
        var pages = new WritePaginator().Pages(session.Document).ToArray();
        Assert.True(pages.Length >= 2);
        Assert.Contains("Column heading", Text(pages[1]));
        Assert.Contains("Item 11", Text(pages[^1]));
    }

    internal static string Text(WritePage page) => string.Concat(page.Commands.OfType<DrawGlyphs>()
        .SelectMany(run => run.Glyphs).Select(glyph => char.ConvertFromUtf32(glyph.Unicode)));
}
