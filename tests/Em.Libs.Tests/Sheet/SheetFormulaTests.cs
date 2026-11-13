using Em.Sheet.Commands;
using Em.Sheet.Cells;
using Xunit;

namespace Em.Libs.Tests.Sheet;

public sealed class SheetFormulaTests
{
    [Theory]
    [InlineData("2+3*4", "14")]
    [InlineData("-2^2", "4")]
    [InlineData("A1+A2", "7")]
    [InlineData("SUM(A1:A2)", "7")]
    [InlineData("SUMIF(A1:A2,\">3\")", "5")]
    [InlineData("IF(FALSE,1/0,8)", "8")]
    [InlineData("IFNA(NA(),3)", "3")]
    [InlineData("XLOOKUP(5,A1:A2,B1:B2)", "second")]
    [InlineData("INDEX(B1:B2,2)", "second")]
    [InlineData("COUNTBLANK(C1:C10)", "10")]
    [InlineData("DATE(1900,2,29)", "60")]
    [InlineData("TEXT(DATE(2026,1,2),\"yyyy-mm-dd\")", "2026-01-02")]
    [InlineData("TEXTJOIN(\",\",TRUE,B1:B2)", "first,second")]
    [InlineData("OFFSET(A1,1,0)", "5")]
    [InlineData("INDIRECT(\"A2\")", "5")]
    [InlineData("HYPERLINK(\"https://example.com\")", "#NAME?")]
    public void Calculates_selected_GridCraft_cases(string formula, string expected)
    {
        var session = new SheetSession();
        session.Paste(default, "2\tfirst\n5\tsecond");
        var cell = new CellAddress(0, 3);
        session.SetCell(cell, "=" + formula);
        Assert.Equal(expected, session.GetValue(session.ActiveSheet, cell).ToString());
    }

    [Fact]
    public void Recalculates_cross_sheet_dependencies_and_detects_cycles()
    {
        var session = new SheetSession();
        var original = session.ActiveSheet;
        session.SetCell(default, "2");
        session.AddSheet("Monthly Sales");
        var added = session.ActiveSheet;
        session.SetCell(default, "='Sheet1'!A1+1");
        Assert.Equal(3, session.GetValue(added, default).Number);

        session.ActivateSheet(original);
        session.Shift(true, 0, 1, true);
        Assert.Equal("'Sheet1'!A2+1", added[default].Formula);
        Assert.Equal(3, session.GetValue(added, default).Number);
        session.Undo();
        Assert.Equal("'Sheet1'!A1+1", added[default].Formula);
        session.SetCell(default, "='Monthly Sales'!A1");
        Assert.Equal(CellError.Circular, session.GetValue(original, default).Error);
        session.SetCell(default, "4");
        Assert.Equal(5, session.GetValue(added, default).Number);
    }

    [Fact]
    public void Fill_adjusts_relative_and_absolute_references()
    {
        var session = new SheetSession();
        session.SetCell(default, "=$B$1+B1");
        var lower = new CellAddress(1, 0);
        session.Fill(new(default, default), new(default, lower), true);
        Assert.Equal("$B$1+B2", session.ActiveSheet[lower].Formula);
    }
}
