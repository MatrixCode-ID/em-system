using System.IO;
using System.Text;
using Em.Sheet.Commands;
using Em.Sheet.Cells;
using Em.Sheet.IO;
using Xunit;

namespace Em.Libs.Tests.Sheet;

public sealed class SheetFileTests
{
    [Fact]
    public void Workbook_round_trip_preserves_formula_styles_dimensions_and_sheets()
    {
        var session = new SheetSession();
        var address = new CellAddress(0, 2);
        session.Paste(default, "4\t5");
        session.SetCell(address, "=SUM(A1:B1)");
        session.Format(new(address, address), style => style with { NumberFormat = "0.00", Bold = true, FillColor = 0xFFAA5555 });
        session.Resize(false, 2, 180);
        session.Resize(true, 0, 44);
        session.Freeze(1, 1);
        session.Merge(new(new(2, 0), new(2, 1)));
        session.AddSheet("History");
        session.HideSheet(session.ActiveSheet, true);
        using var stream = new MemoryStream();
        SheetFiles.SaveXlsx(session.Workbook, stream);
        stream.Position = 0;
        var result = SheetFiles.OpenXlsx(stream);
        Assert.Empty(result.Warnings);
        var workbook = result.Workbook;
        Assert.Equal(2, workbook.Sheets.Count);
        Assert.True(workbook.Sheets[1].IsHidden);
        var sheet = workbook.Sheets[0];
        Assert.Equal(9, sheet[address].Value.Number);
        Assert.Equal("SUM(A1:B1)", sheet[address].Formula);
        Assert.Equal(session.Workbook.Styles[session.ActiveSheet[address].StyleIndex], workbook.Styles[sheet[address].StyleIndex]);
        Assert.Equal(180, sheet.ColumnWidths[2], 6);
        Assert.Equal(44, sheet.RowHeights[0], 6);
        Assert.Equal(1, sheet.FrozenRows);
        Assert.Equal(1, sheet.FrozenColumns);
        Assert.Equal("A3:B3", Assert.Single(sheet.Merges).ToString());
    }

    [Fact]
    public void Csv_round_trip_preserves_delimiters_quotes_and_line_breaks()
    {
        const string csv = "name;value\r\n\"quoted;cell\";\"line\nwith \"\"quotes\"\"\"\r\n";
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var workbook = SheetFiles.OpenCsv(input, ';').Workbook;
        using var output = new MemoryStream();
        SheetFiles.SaveCsv(workbook, workbook.Sheets[0], output, ';');
        output.Position = 0;
        var roundTrip = SheetFiles.OpenCsv(output, ';').Workbook.Sheets[0];
        Assert.Equal("quoted;cell", roundTrip[new(1, 0)].Value.Text);
        Assert.Equal("line\nwith \"quotes\"", roundTrip[new(1, 1)].Value.Text);
    }

    [Fact]
    public void Viewport_is_bounded_at_distant_rows_and_keeps_frozen_cells()
    {
        var session = new SheetSession();
        session.Freeze(1, 1);
        var viewport = Em.Sheet.View.SheetViewport.Create(session, 999_990, 25, 600, 320);
        Assert.Equal(0, viewport.Rows[0].Index);
        Assert.Equal(999_990, viewport.Rows[1].Index);
        Assert.Equal(new CellAddress(999_990, 25), viewport.HitTest(145, 57));
        Assert.InRange(viewport.Cells.Count, 1, 100);
    }
}
