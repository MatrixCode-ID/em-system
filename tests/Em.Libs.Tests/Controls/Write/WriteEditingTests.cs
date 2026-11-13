using Em.Editing;
using Em.Controls.Write.Editing;
using Em.Controls.Write.Model;
using Xunit;

namespace Em.Libs.Tests.Controls.Write;

public sealed class WriteEditingTests
{
    [Fact]
    public void Typing_and_delete_preserve_graphemes_and_undo_caret()
    {
        var session = new WriteSession();
        session.InsertText("Hi 👩‍💻");
        Assert.Equal("Hi 👩‍💻", session.GetParagraph(session.Selection.Caret)!.Text);
        Assert.True(session.Delete(true).Accepted);
        Assert.Equal("Hi ", session.GetParagraph(session.Selection.Caret)!.Text);
        Assert.True(session.Undo().Accepted);
        Assert.Equal("Hi 👩‍💻".Length, session.Selection.Caret.Offset);
        Assert.True(session.Redo().Accepted);
        Assert.Equal(3, session.Selection.Caret.Offset);
    }

    [Fact]
    public void Runs_and_paragraph_styles_inherit_with_numbering()
    {
        var session = new WriteSession(WriteDocument.Create("Alpha\nBeta"));
        session.SelectAll();
        session.ApplyStyle("Heading1", WriteStyleKind.Paragraph);
        session.ApplyStyle("Emphasis", WriteStyleKind.Character);
        session.SetList(WriteListKind.Decimal);
        var first = session.GetParagraph(session.Selection.Start)!;
        var resolved = session.Document.Resolve(first, Assert.Single(first.Runs));
        Assert.True(resolved.Bold);
        Assert.True(resolved.Italic);
        Assert.True(session.Document.Resolve(first).Paragraph.KeepWithNext);
        var definition = session.Document.Numbering[first.NumberingId!.Value];
        Assert.Equal("1.", definition.Format(0));
        Assert.Equal("2.", definition.Format(1));
        Assert.Equal("Alpha\nBeta", session.Copy().Text);
        Assert.True(session.Undo().Accepted);
        Assert.Null(session.GetParagraph(session.Selection.Start)!.NumberingId);
    }

    [Fact]
    public void Table_structure_and_merge_are_undoable()
    {
        var session = new WriteSession();
        session.InsertTable(2, 3);
        session.InsertText("Item");
        session.SetHeaderRepeat(true);
        session.ChangeRows(1, true);
        session.ChangeColumns(3, true);
        var position = new WritePosition(0, 1, 0, Row: 1, Cell: 2);
        session.Select(new(position, position));
        Assert.True(session.MergeCells(2).Accepted);
        var table = Assert.IsType<WriteTable>(session.Document.Sections[0].Blocks[1]);
        Assert.Equal(3, table.Rows.Length);
        Assert.Equal(4, table.ColumnWidths.Length);
        Assert.Equal(2, table.Rows[1].Cells[3 - 1].ColumnSpan);
        session.Undo();
        table = Assert.IsType<WriteTable>(session.Document.Sections[0].Blocks[1]);
        Assert.All(table.Rows[1].Cells, cell => Assert.Equal(1, cell.ColumnSpan));
    }

    [Fact]
    public void Viewer_rejects_all_document_edits()
    {
        var session = new WriteSession(WriteDocument.Create("One"));
        session.SelectAll();
        var copy = session.Copy();
        session.Mode = EditorMode.Viewer;
        session.DocumentChanged += (_, _) => Assert.Fail("Viewer mutated the document.");
        CommandResult[] results = [
            session.InsertText("Other"), session.Delete(true), session.Cut(), session.Paste(copy), session.Undo(), session.Redo(),
            session.ApplyStyle("Title", WriteStyleKind.Paragraph), session.FormatParagraphs(style => style),
            session.FormatCharacters(style => style), session.SetList(WriteListKind.Bullet), session.InsertTable(2, 2),
            session.ChangeRows(0, true), session.ChangeColumns(0, true), session.MergeCells(2), session.SplitCell(),
            session.SetHeaderRepeat(true), session.ResizeColumn(0, 100), session.FormatCell(0, 0),
            session.EditStory(WriteStory.Footer, 0), session.InsertBreak(true), session.SetPage(new()),
            session.ResizeImage(100, 100), session.InsertRun(new("", Field: WriteField.Page)), session.ReplaceAll("One", "Two")
        ];
        Assert.All(results, result => Assert.False(result.Accepted));
        Assert.Equal("One", session.Copy().Text);
    }
}
