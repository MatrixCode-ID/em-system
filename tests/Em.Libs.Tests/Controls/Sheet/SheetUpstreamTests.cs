using System.IO;
using System.Text;
using Em.Controls.Sheet.Commands;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.IO;
using Xunit;

namespace Em.Libs.Tests.Controls.Sheet;

public sealed class SheetUpstreamTests
{
    [Fact]
    public void Dense_range_dimensions_are_bounded_by_the_grid()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CellRange(default, new(int.MaxValue, int.MaxValue)));
        var range = new CellRange(default, new(CellAddress.MaxRows - 1, CellAddress.MaxColumns - 1));
        Assert.Equal(17_179_869_184, range.Count);
    }

    [Fact]
    public void Deep_postfix_wrappers_produce_a_formula_error()
    {
        var session = new SheetSession();
        session.SetCell(default, "=1" + new string('%', 4096));
        Assert.Equal(CellError.Value, session.GetValue(session.ActiveSheet, default).Error);
    }

    [Fact]
    public void Csv_discards_out_of_bounds_fields_without_losing_the_next_record()
    {
        string csv = new string(',', CellAddress.MaxColumns) + "\"discard,including\nnewline\"\r\nkeep,2";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var sheet = SheetFiles.OpenCsv(stream).Workbook.Sheets[0];
        Assert.Equal("keep", sheet[new(1, 0)].Value.Text);
        Assert.Equal(2, sheet[new(1, 1)].Value.Number);
    }

    [Fact]
    public void Merged_cells_are_one_step_and_autofit_ignores_them()
    {
        var session = new SheetSession();
        session.SetCell(default, "Merged wide text");
        session.Merge(new(default, new CellAddress(1, 1)));
        Assert.Equal(new CellAddress(0, 2), SheetNavigation.Step(session.ActiveSheet, default, 0, 1));
        Assert.Equal(new CellAddress(2, 0), SheetNavigation.Step(session.ActiveSheet, default, 1, 0));
        session.SetCell(new(2, 0), "short");
        session.SetCell(new(3, 0), "short");
        int calls = 0;
        session.AutoFitColumns(new(default, default), (text, _) => { calls++; return text.Length * 10; });
        Assert.Equal(1, calls);
        Assert.Equal(66, session.ActiveSheet.ColumnWidths[0]);
        session.Undo();
        Assert.Empty(session.ActiveSheet.ColumnWidths);
    }
}
