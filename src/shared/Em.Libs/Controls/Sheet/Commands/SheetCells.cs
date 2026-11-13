using System.Globalization;
using System.Text.RegularExpressions;
using Em.Editing;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Formulas;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.Commands;

/// <summary>What Paste Special writes from the copied cells.</summary>
public enum SheetPastePart
{
    /// <summary>Values or formulas together with the copied formats.</summary>
    All,
    /// <summary>The shown values only; the target cells keep their formats.</summary>
    Values,
    /// <summary>Formulas and constants; the target cells keep their formats.</summary>
    Formulas,
    /// <summary>The copied formats only; the target cells keep their contents.</summary>
    Formats
}

public sealed partial class SheetSession
{
    /// <summary>
    /// Inserts blank cells over <paramref name="range"/>, or deletes it, shifting the cells below it (with
    /// <paramref name="down"/>) or right of it within its columns or rows. References to the shifted cells follow them.
    /// </summary>
    public CommandResult ShiftCells(CellRange range, bool down, bool insert)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var sheet = ActiveSheet;
        bool rows = down;
        int start = rows ? range.Start.Row : range.Start.Column, count = rows ? range.Height : range.Width;
        int bandStart = rows ? range.Start.Column : range.Start.Row, bandEnd = rows ? range.End.Column : range.End.Row;
        int limit = rows ? CellAddress.MaxRows : CellAddress.MaxColumns;
        var region = rows
            ? new CellRange(new CellAddress(start, bandStart), new CellAddress(CellAddress.MaxRows - 1, bandEnd))
            : new CellRange(new CellAddress(bandStart, start), new CellAddress(bandEnd, CellAddress.MaxColumns - 1));
        if (sheet.Merges.Any(merge => merge.Intersects(region)))
            return Invalid("Cells in a merged range cannot be shifted.");

        int Position(CellAddress address) => rows ? address.Row : address.Column;
        CellAddress At(CellAddress address, int position) => rows ? address with { Row = position } : address with { Column = position };
        string Adjust(SheetTab tab, string formula) =>
            ReferenceAdjustment.ShiftBand(formula, tab.Name, sheet.Name, rows, start, count, insert, bandStart, bandEnd);

        var after = new Dictionary<(SheetTab Tab, CellAddress Address), SheetCell>();
        var moving = sheet.Cells.Where(pair => region.Contains(pair.Key)).ToArray();
        foreach (var (address, _) in moving)
            after[(sheet, address)] = default;
        foreach (var (address, cell) in moving)
        {
            int position = Position(address);
            int next = insert ? position + count : position < start + count ? -1 : position - count;
            if (next >= 0 && next < limit)
                after[(sheet, At(address, next))] = cell.Formula is { } formula ? cell with { Formula = Adjust(sheet, formula) } : cell;
        }
        foreach (var tab in Workbook.Sheets)
        foreach (var (address, cell) in tab.Cells)
        {
            if (cell.Formula is not { } formula || (ReferenceEquals(tab, sheet) && region.Contains(address)))
                continue;
            string adjusted = Adjust(tab, formula);
            if (adjusted != formula)
                after[(tab, address)] = cell with { Formula = adjusted };
        }
        return ChangeCells(after.Select(pair => (pair.Key.Tab, pair.Key.Address, pair.Value)));
    }

    /// <summary>Pastes part of a copy: values, formulas or formats, optionally swapping its rows and columns.</summary>
    public CommandResult PasteSpecial(CellAddress origin, SheetClipboard clipboard, SheetPastePart part, bool transpose = false)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (!origin.IsValid || clipboard.Rows <= 0 || clipboard.Columns <= 0 ||
            (long)clipboard.Rows * clipboard.Columns != clipboard.Cells.Count)
            return Invalid("Invalid clipboard dimensions.");

        var sheet = ActiveSheet;
        return ChangeCells(clipboard.Cells.Select((cell, i) =>
            {
                int row = i / clipboard.Columns, column = i % clipboard.Columns;
                var target = transpose ? new CellAddress(origin.Row + column, origin.Column + row)
                    : new CellAddress(origin.Row + row, origin.Column + column);
                return (target, cell);
            })
            .Where(item => item.target.IsValid)
            .Select(item =>
            {
                var existing = sheet[item.target];
                var pasted = part switch
                {
                    SheetPastePart.Values => new SheetCell(item.cell.Value, null, existing.StyleIndex),
                    SheetPastePart.Formulas => item.cell with { StyleIndex = existing.StyleIndex },
                    SheetPastePart.Formats => existing with { StyleIndex = item.cell.StyleIndex },
                    _ => item.cell
                };
                return (item.target, pasted);
            }));
    }

    /// <summary>
    /// Sorts the rows of <paramref name="range"/> by the values in <paramref name="column"/>: numbers before text, text before
    /// logical values and errors, and blanks last in either order. Relative references move with their rows.
    /// </summary>
    public CommandResult Sort(CellRange range, int column, bool descending)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (column < range.Start.Column || column > range.End.Column)
            return Invalid("The sort column must be inside the range.");
        if (range.Count > 1_000_000)
            return Invalid("Sorting is limited to one million cells per command.");
        var sheet = ActiveSheet;
        if (sheet.Merges.Any(merge => merge.Intersects(range)))
            return Invalid("A range with merged cells cannot be sorted.");

        var keys = Enumerable.Range(range.Start.Row, range.Height)
            .Select(row => (Row: row, Key: GetValue(sheet, new CellAddress(row, column)))).ToArray();
        var blanks = keys.Where(item => item.Key.Kind == CellValueKind.Blank);
        var filled = keys.Where(item => item.Key.Kind != CellValueKind.Blank);
        var ordered = (descending ? filled.OrderByDescending(item => item.Key, SortComparer.Instance)
            : filled.OrderBy(item => item.Key, SortComparer.Instance)).Concat(blanks).Select(item => item.Row).ToArray();

        var changes = new List<(CellAddress, SheetCell)>();
        for (int i = 0; i < ordered.Length; i++)
        {
            int target = range.Start.Row + i, source = ordered[i];
            if (target == source)
                continue;
            for (int c = range.Start.Column; c <= range.End.Column; c++)
            {
                var cell = sheet[new CellAddress(source, c)];
                if (cell.Formula is { } formula)
                    cell = cell with { Formula = ReferenceAdjustment.Copy(formula, target - source, 0), Value = default };
                changes.Add((new CellAddress(target, c), cell));
            }
        }
        return ChangeCells(changes);
    }

    /// <summary>The block of filled cells around a cell, bounded by blank rows and columns, as Ctrl+A or a sort finds it.</summary>
    public CellRange CurrentRegion(CellAddress cell)
    {
        var sheet = ActiveSheet;
        int top = cell.Row, bottom = cell.Row, left = cell.Column, right = cell.Column;
        bool Filled(int row, int column) => row >= 0 && column >= 0 && row < CellAddress.MaxRows && column < CellAddress.MaxColumns &&
            sheet[new CellAddress(row, column)] is var value && (value.Formula is not null || value.Value.Kind != CellValueKind.Blank);
        bool RowFilled(int row) => Enumerable.Range(Math.Max(0, left - 1), right - Math.Max(0, left - 1) + 2).Any(column => Filled(row, column));
        bool ColumnFilled(int column) => Enumerable.Range(Math.Max(0, top - 1), bottom - Math.Max(0, top - 1) + 2).Any(row => Filled(row, column));
        for (int guard = 0; guard < 100_000; guard++)
        {
            bool grown = false;
            if (top > 0 && RowFilled(top - 1)) { top--; grown = true; }
            if (bottom < CellAddress.MaxRows - 1 && RowFilled(bottom + 1)) { bottom++; grown = true; }
            if (left > 0 && ColumnFilled(left - 1)) { left--; grown = true; }
            if (right < CellAddress.MaxColumns - 1 && ColumnFilled(right + 1)) { right++; grown = true; }
            if (!grown)
                break;
        }
        return new CellRange(new CellAddress(top, left), new CellAddress(bottom, right));
    }

    /// <summary>The distinct text entries in the unbroken list above and below a cell, for Pick From Drop-down List.</summary>
    public IReadOnlyList<string> ListValues(CellAddress cell)
    {
        var sheet = ActiveSheet;
        var values = new List<string>();
        bool Take(int row)
        {
            if (row < 0 || row >= CellAddress.MaxRows)
                return false;
            var address = new CellAddress(row, cell.Column);
            var source = sheet[address];
            if (source.Formula is null && source.Value.Kind == CellValueKind.Blank)
                return false;
            if (GetValue(sheet, address) is { Kind: CellValueKind.Text, Text: { Length: > 0 } text })
                values.Add(text);
            return true;
        }
        for (int row = cell.Row - 1; row >= 0 && values.Count < 100_000 && Take(row); row--) { }
        for (int row = cell.Row + 1; values.Count < 100_000 && Take(row); row++) { }
        return values.Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    /// <summary>Gives a cell, range or formula a workbook-level name; an existing name is redefined.</summary>
    public CommandResult DefineName(string name, string refersTo)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        name = name.Trim();
        string formula = refersTo.Trim().TrimStart('=');
        if (!NamePattern().IsMatch(name) || CellAddress.TryParse(name, out _) || name.Equals("R", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("C", StringComparison.OrdinalIgnoreCase))
            return Invalid("The name must start with a letter or underscore, contain no spaces and not look like a cell reference.");
        if (formula.Length == 0)
            return Invalid("Enter what the name refers to.");

        var names = Workbook.MutableNames;
        bool existed = names.TryGetValue(name, out var previous);
        if (existed && previous == formula)
            return CommandResult.Success;
        return Change(() => names[name] = formula, () =>
        {
            if (existed)
                names[name] = previous!;
            else
                names.Remove(name);
        });
    }

    [GeneratedRegex(@"^[\p{L}_\\][\p{L}\p{N}_.]{0,254}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    private CommandResult ChangeCells(IEnumerable<(SheetTab Tab, CellAddress Address, SheetCell Value)> changes)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        var cells = changes.Select(change => (change.Tab, change.Address, Before: change.Tab[change.Address], After: change.Value))
            .Where(change => change.Before != change.After).ToArray();
        if (cells.Length == 0)
            return CommandResult.Success;
        return Change(() =>
        {
            foreach (var change in cells)
                change.Tab.Set(change.Address, change.After);
        }, () =>
        {
            foreach (var change in cells)
                change.Tab.Set(change.Address, change.Before);
        });
    }

    /// <summary>Sort order of values: numbers, then text without regard to case, then logical values, then errors.</summary>
    private sealed class SortComparer : IComparer<CellValue>
    {
        public static readonly SortComparer Instance = new();

        public int Compare(CellValue x, CellValue y)
        {
            int Rank(CellValue value) => value.Kind switch
            {
                CellValueKind.Number => 0, CellValueKind.Text => 1, CellValueKind.Boolean => 2, _ => 3
            };
            int rank = Rank(x).CompareTo(Rank(y));
            if (rank != 0)
                return rank;
            return x.Kind switch
            {
                CellValueKind.Number or CellValueKind.Boolean => x.Number.CompareTo(y.Number),
                CellValueKind.Text => string.Compare(x.Text, y.Text, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase),
                _ => 0
            };
        }
    }
}
