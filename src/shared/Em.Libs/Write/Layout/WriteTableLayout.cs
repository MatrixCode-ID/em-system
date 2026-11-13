// Ported from WordCraft (layout/src/table_flow.rs @ 060768f1ed7f310e639bc49d7af7256e129f7409), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the WordCraft contributors.
using Em.Documents.Layout;
using Em.Write.Editing;
using Em.Write.Model;

namespace Em.Write.Layout;

public sealed partial class WritePaginator
{
    private List<TableBand> MeasureRow(WriteDocument document, WriteTable table, WriteTableRow row, WritePosition position,
        double x, double width, int page, int? total)
    {
        const double padding = 5;
        var cells = new List<TableCellLayout>();
        double gridWidth = table.ColumnWidths.Sum(item => Math.Max(1, item));
        int column = 0;
        for (int i = 0; i < row.Cells.Length; i++)
        {
            var cell = row.Cells[i];
            var span = Math.Max(1, cell.ColumnSpan);
            double actual = gridWidth > 0 ? table.ColumnWidths.Skip(column).Take(span).Sum(item => Math.Max(1, item)) / gridWidth * width : width;
            actual = Math.Max(12, actual);
            var lines = new List<Line>();
            for (int block = 0; block < cell.Blocks.Length; block++)
                if (cell.Blocks[block] is WriteParagraph paragraph)
                    lines.AddRange(MeasureParagraph(document, paragraph, position with { Cell = i, CellBlock = block },
                        actual - padding * 2, page, total, null));
            cells.Add(new(x, actual, cell, lines));
            x += actual;
            column += span;
        }

        var result = new List<TableBand>();
        int bands = cells.Select(cell => cell.Lines.Count).DefaultIfEmpty(1).Max();
        for (int i = 0; i < Math.Max(1, bands); i++)
        {
            double height = cells.Where(cell => i < cell.Lines.Count).Select(cell => cell.Lines[i].Height).DefaultIfEmpty(18).Max();
            result.Add(new(cells, i, height + padding * 2));
        }
        return result;
    }

    private void PlaceRow(PageBuilder page, TableBand row, int? total)
    {
        double top = page.Y, bottom = top + row.Height;
        foreach (var cell in row.Cells)
        {
            if (cell.Cell.Fill is { } fill)
                page.Draw.Add(new FillRectangle(cell.X, top, cell.Width, row.Height, Color(fill)));
            if (cell.Cell.NoBorder)
            {
                if (row.Index < cell.Lines.Count)
                    PlaceLine(page, cell.Lines[row.Index], cell.X + 5, top + 5, page.Number, total);
                continue;
            }
            var ink = cell.Cell.Border is null ? DocumentColor.Outline : Color(cell.Cell.Border);
            page.Draw.Add(new DrawLine(cell.X, top, cell.X, bottom, .8, ink));
            page.Draw.Add(new DrawLine(cell.X + cell.Width, top, cell.X + cell.Width, bottom, .8, ink));
            if (row.Index == 0)
                page.Draw.Add(new DrawLine(cell.X, top, cell.X + cell.Width, top, .8, ink));
            if (row.Index == row.Cells.Max(item => item.Lines.Count) - 1)
                page.Draw.Add(new DrawLine(cell.X, bottom, cell.X + cell.Width, bottom, .8, ink));
            if (row.Index < cell.Lines.Count)
                PlaceLine(page, cell.Lines[row.Index], cell.X + 5, top + 5, page.Number, total);
        }
        page.Y = bottom;
    }

    private sealed record TableCellLayout(double X, double Width, WriteTableCell Cell, List<Line> Lines);
    private sealed record TableBand(List<TableCellLayout> Cells, int Index, double Height);
}
