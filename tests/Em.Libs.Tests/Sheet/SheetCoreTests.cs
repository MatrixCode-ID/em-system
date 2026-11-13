using Em.Editing;
using Em.Sheet.Commands;
using Em.Sheet.Cells;
using Em.Sheet.NumberFormats;
using Xunit;

namespace Em.Libs.Tests.Sheet;

public sealed class SheetCoreTests
{
    [Theory]
    [InlineData("A1", "A1")]
    [InlineData("$az$56", "AZ56")]
    [InlineData("XFD1048576", "XFD1048576")]
    public void Addresses_round_trip(string input, string canonical)
    {
        Assert.True(CellAddress.TryParse(input, out var address));
        Assert.Equal(canonical, address.ToString());
    }

    [Theory]
    [InlineData("A0")]
    [InlineData("XFE1")]
    [InlineData("XFD1048577")]
    [InlineData("A1 ")]
    public void Invalid_addresses_are_rejected(string input) => Assert.False(CellAddress.TryParse(input, out _));

    [Theory]
    [InlineData("0.00", 2.675, "2.68")]
    [InlineData("0.00", 1.005, "1.01")]
    [InlineData("#,##0", -1234567.89, "-1,234,568")]
    [InlineData("0.00%", .12345, "12.35%")]
    [InlineData("#,##0,", 1234567.0, "1,235")]
    [InlineData("#,##0.00;[Red](#,##0.00)", -5.0, "(5.00)")]
    [InlineData("[>=100]\"big\";[<0]\"neg\";\"small\"", 50, "small")]
    [InlineData("yyyy-mm-dd", 60, "1900-02-29")]
    [InlineData("h:mm AM/PM", .5625, "1:30 PM")]
    public void GridCraft_number_cases(string format, double number, string expected) =>
        Assert.Equal(expected, SheetNumberFormats.Format(CellValue.From(number), format));

    [Fact]
    public void Sparse_changes_and_formatting_are_undoable()
    {
        var session = new SheetSession();
        var cell = new CellAddress(12, 0);
        var range = new CellRange(cell, new(12, 1));
        Assert.True(session.SetCell(cell, "42").Accepted);
        Assert.True(session.Format(range, style => style with { Bold = true }).Accepted);
        Assert.Equal(2, session.Workbook.Styles.Count);
        Assert.Equal(session.ActiveSheet[cell].StyleIndex, session.ActiveSheet[range.End].StyleIndex);
        Assert.True(session.Undo().Accepted);
        Assert.Single(session.ActiveSheet.Cells);
        Assert.True(session.Undo().Accepted);
        Assert.Empty(session.ActiveSheet.Cells);
        Assert.True(session.Redo().Accepted);
        Assert.Equal(42, session.ActiveSheet[cell].Value.Number);
    }

    [Fact]
    public void Structure_fill_and_clipboard_round_trip_through_undo()
    {
        var session = new SheetSession();
        var first = new CellAddress(0, 0);
        var second = new CellAddress(1, 0);
        var third = new CellAddress(2, 0);
        Assert.True(session.Paste(first, "1\n3").Accepted);
        Assert.True(session.Fill(new(first, second), new(first, third)).Accepted);
        Assert.Equal(5, session.ActiveSheet[third].Value.Number);
        Assert.True(session.Merge(new(second, third)).Accepted);
        Assert.True(session.Shift(true, 0, 1, true).Accepted);
        Assert.True(session.Resize(false, 0, 148).Accepted);
        Assert.True(session.Freeze(1, 2).Accepted);
        var original = session.ActiveSheet;
        Assert.True(session.AddSheet("Orders").Accepted);
        var added = session.ActiveSheet;
        Assert.True(session.MoveSheet(added, 0).Accepted);
        Assert.True(session.RenameSheet(added, "Sales").Accepted);
        Assert.True(session.HideSheet(added, true).Accepted);
        Assert.Same(original, session.ActiveSheet);
        Assert.True(session.DeleteSheet(added).Accepted);

        for (int i = 0; i < 9; i++)
            Assert.True(session.Undo().Accepted);
        Assert.Same(original, session.ActiveSheet);
        Assert.Single(session.Workbook.Sheets);
        Assert.Empty(original.RowHeights);
        Assert.Empty(original.ColumnWidths);
        Assert.Equal(0, original.FrozenColumns);
        Assert.Empty(original.Merges);
        Assert.Equal(5, original[third].Value.Number);
        Assert.Contains("<td>5</td>", session.Copy(new(first, third)).Html);
    }

    [Fact]
    public void Viewer_rejects_engine_mutations_and_preserves_undo()
    {
        var session = new SheetSession();
        var cell = new CellAddress(0, 0);
        var range = new CellRange(cell, new(0, 1));
        Assert.True(session.SetCell(cell, "1").Accepted);
        Assert.True(session.AddSheet("Extra").Accepted);
        session.Mode = EditorMode.Viewer;
        session.DocumentChanged += (_, _) => Assert.Fail("Viewer mutation");
        var sheet = session.ActiveSheet;
        CommandResult[] results = [
            session.SetCell(cell, "5"), session.Clear(range), session.Format(range, style => style with { Italic = true }),
            session.AddSheet("New"), session.RenameSheet(sheet, "Renamed"), session.MoveSheet(sheet, 0),
            session.HideSheet(sheet, true), session.DeleteSheet(sheet), session.Resize(true, 0, 30),
            session.Freeze(1, 1), session.Merge(range), session.Shift(true, 0, 1, false),
            session.Fill(new(cell, cell), range), session.Paste(cell, "2"),
            session.Paste(cell, session.Copy(range)), session.Undo(), session.Redo()
        ];
        Assert.All(results, result => Assert.False(result.Accepted));
        Assert.False(session.CanUndo);
        session.Mode = EditorMode.Full;
        Assert.True(session.CanUndo);
    }
}
