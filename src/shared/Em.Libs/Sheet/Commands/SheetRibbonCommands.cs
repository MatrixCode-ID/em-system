using Em.Editing;
using Em.Sheet.Cells;
using Em.Sheet.Model;

namespace Em.Sheet.Commands;

/// <summary>Range commands behind the GridCraft Home ribbon that each record a single undo step.</summary>
public sealed partial class SheetSession
{
    /// <summary>Formats every cell of <paramref name="range"/> with a style that may depend on the cell's position.</summary>
    public CommandResult FormatEach(CellRange range, Func<CellAddress, SheetStyle, SheetStyle> format)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (range.Count > 1_000_000)
            return Invalid("Formatting is limited to one million cells per command.");
        return ChangeCells(range.Cells().Select(address =>
        {
            var cell = ActiveSheet[address];
            return (address, cell with { StyleIndex = Workbook.Intern(format(address, Workbook.Styles[cell.StyleIndex])) });
        }));
    }

    /// <summary>Resets the formatting of the cells in <paramref name="range"/> and keeps their values and formulas.</summary>
    public CommandResult ClearFormats(CellRange range) => ChangeCells(ActiveSheet.Cells
        .Where(pair => range.Contains(pair.Key) && pair.Value.StyleIndex != 0)
        .Select(pair => (pair.Key, pair.Value with { StyleIndex = 0 })).ToArray());

    /// <summary>Removes values, formulas and formatting from the cells in <paramref name="range"/>.</summary>
    public CommandResult ClearAll(CellRange range) => ChangeCells(ActiveSheet.Cells
        .Where(pair => range.Contains(pair.Key)).Select(pair => (pair.Key, default(SheetCell))).ToArray());

    /// <summary>Enters several inputs as one undo step. Each input is parsed like typing into the cell.</summary>
    public CommandResult SetCells(IEnumerable<(CellAddress Address, string Input)> inputs)
    {
        var changes = new List<(CellAddress, SheetCell)>();
        foreach (var (address, input) in inputs)
        {
            if (!address.IsValid)
                return Invalid("Invalid cell address.");
            changes.Add((address, SheetCell.FromInput(input, ActiveSheet[address].StyleIndex)));
        }
        return ChangeCells(changes);
    }

    /// <summary>Merges each row of <paramref name="range"/> separately, as Excel's Merge Across does.</summary>
    public CommandResult MergeAcross(CellRange range)
    {
        var sheet = ActiveSheet;
        if (range.Width < 2)
            return Invalid("Select more than one column to merge across.");
        if (sheet.Merges.Any(range.Intersects))
            return Invalid("Unmerge the selected cells first.");
        if (range.Height > 100_000)
            return Invalid("Merging is limited to 100,000 rows per command.");
        var rows = Enumerable.Range(range.Start.Row, range.Height)
            .Select(row => new CellRange(new(row, range.Start.Column), new(row, range.End.Column))).ToArray();
        var overwritten = sheet.Cells.Where(cell => range.Contains(cell.Key) && cell.Key.Column != range.Start.Column).ToArray();

        void Apply()
        {
            sheet.MutableMerges.AddRange(rows);
            foreach (var item in overwritten)
                sheet.Set(item.Key, default);
        }

        void Revert()
        {
            foreach (var row in rows)
                sheet.MutableMerges.Remove(row);
            foreach (var item in overwritten)
                sheet.Set(item.Key, item.Value);
        }

        return Change(Apply, Revert);
    }
}
