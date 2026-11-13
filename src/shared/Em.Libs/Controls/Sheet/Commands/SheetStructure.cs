// Ported from GridCraft (engine/src/cmd/sheet/tabs.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
// Upstream sync: GridCraft crates/engine/src/cmd/format/sizes.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926.
using Em.Editing;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Formulas;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.Commands;

public sealed partial class SheetSession
{
    /// <summary>Adds a sheet at the end and activates it.</summary>
    /// <param name="name">A valid, unique sheet name.</param>
    /// <returns>The command result.</returns>
    public CommandResult AddSheet(string name)
    {
        if (!Workbook.IsValidName(name))
            return Invalid("Invalid or duplicate sheet name.");

        var sheet = new SheetTab(name);
        return Change(() => { Workbook.MutableSheets.Add(sheet); ActiveSheet = sheet; },
            () => Workbook.MutableSheets.Remove(sheet));
    }

    /// <summary>Renames a sheet.</summary>
    /// <param name="sheet">The sheet to rename.</param>
    /// <param name="name">A valid, unique sheet name.</param>
    /// <returns>The command result.</returns>
    public CommandResult RenameSheet(SheetTab sheet, string name)
    {
        if (!Workbook.Sheets.Contains(sheet) || !Workbook.IsValidName(name, sheet))
            return Invalid("Invalid or duplicate sheet name.");

        string old = sheet.Name;
        return Change(() => sheet.Name = name, () => sheet.Name = old);
    }

    /// <summary>Deletes a sheet; the last visible sheet cannot be deleted.</summary>
    /// <param name="sheet">The sheet to delete.</param>
    /// <returns>The command result.</returns>
    public CommandResult DeleteSheet(SheetTab sheet)
    {
        int index = Workbook.MutableSheets.IndexOf(sheet);
        if (index < 0 || (!sheet.IsHidden && Workbook.Sheets.Count(tab => !tab.IsHidden) == 1))
            return Invalid("The workbook must have a visible sheet.");
        return Change(() => Workbook.MutableSheets.RemoveAt(index), () => Workbook.MutableSheets.Insert(index, sheet));
    }

    /// <summary>Hides or shows a sheet; the last visible sheet cannot be hidden.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="hidden">True to hide the sheet.</param>
    /// <returns>The command result.</returns>
    public CommandResult HideSheet(SheetTab sheet, bool hidden)
    {
        if (!Workbook.Sheets.Contains(sheet) || (hidden && !sheet.IsHidden && Workbook.Sheets.Count(tab => !tab.IsHidden) == 1))
            return Invalid("The workbook must have a visible sheet.");
        bool old = sheet.IsHidden;
        return Change(() => sheet.IsHidden = hidden, () => sheet.IsHidden = old);
    }

    /// <summary>Moves a sheet to another position in the tab order.</summary>
    /// <param name="sheet">The sheet to move.</param>
    /// <param name="target">The zero-based target position.</param>
    /// <returns>The command result.</returns>
    public CommandResult MoveSheet(SheetTab sheet, int target)
    {
        int index = Workbook.MutableSheets.IndexOf(sheet);
        if (index < 0 || (uint)target >= Workbook.Sheets.Count)
            return Invalid("Invalid sheet position.");

        void Move(int from, int to)
        {
            Workbook.MutableSheets.RemoveAt(from);
            Workbook.MutableSheets.Insert(to, sheet);
        }

        return Change(() => Move(index, target), () => Move(target, index));
    }

    /// <summary>Freezes rows and columns of the active sheet; 0 removes the freeze on that axis.</summary>
    /// <param name="rows">The number of frozen rows.</param>
    /// <param name="columns">The number of frozen columns.</param>
    /// <returns>The command result.</returns>
    public CommandResult Freeze(int rows, int columns)
    {
        if ((uint)rows >= CellAddress.MaxRows || (uint)columns >= CellAddress.MaxColumns)
            return Invalid("Invalid freeze boundary.");
        var sheet = ActiveSheet;
        int oldRows = sheet.FrozenRows, oldColumns = sheet.FrozenColumns;
        return Change(() => (sheet.FrozenRows, sheet.FrozenColumns) = (rows, columns),
            () => (sheet.FrozenRows, sheet.FrozenColumns) = (oldRows, oldColumns));
    }

    /// <summary>Sets the height of a row or the width of a column.</summary>
    /// <param name="rows">True for a row, false for a column.</param>
    /// <param name="index">The zero-based row or column index.</param>
    /// <param name="size">The new size, or null to restore the default.</param>
    /// <returns>The command result.</returns>
    public CommandResult Resize(bool rows, int index, double? size)
    {
        if ((uint)index >= (rows ? CellAddress.MaxRows : CellAddress.MaxColumns) ||
            size is { } value && (!double.IsFinite(value) || value <= 0))
            return Invalid("Invalid row or column size.");

        var sizes = ActiveSheet.MutableSizes(rows);
        double? old = sizes.TryGetValue(index, out double previous) ? previous : null;
        void Apply(double? current)
        {
            if (current is { } specified)
                sizes[index] = specified;
            else
                sizes.Remove(index);
        }
        return Change(() => Apply(size), () => Apply(old));
    }

    /// <summary>Sizes the columns of a range to fit their displayed text.</summary>
    /// <param name="range">The range whose columns are sized.</param>
    /// <param name="measure">Measures the width of a text in a style.</param>
    /// <returns>The command result.</returns>
    public CommandResult AutoFitColumns(CellRange range, Func<string, SheetStyle, double> measure)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;

        var sheet = ActiveSheet;
        var previous = new Dictionary<int, double?>();
        var widths = new Dictionary<int, double>();
        var memo = new Dictionary<(int Style, string Text), double>();
        var mergedColumns = sheet.Merges.SelectMany(merge => Enumerable.Range(merge.Start.Column, merge.Width)
            .Select(column => (column, merge))).ToLookup(item => item.column, item => item.merge);
        foreach (var (address, cell) in sheet.Cells)
        {
            if (address.Column < range.Start.Column || address.Column > range.End.Column ||
                mergedColumns[address.Column].Any(merge => merge.Contains(address)))
                continue;
            string text = GetValue(sheet, address).ToString();
            if (text.Length == 0)
                continue;
            var style = Workbook.Styles[cell.StyleIndex];
            var key = (cell.StyleIndex, text);
            if (!memo.TryGetValue(key, out double width))
            {
                width = Math.Clamp(measure(text, style) + 16 + style.Indent * 4, 8, 1785);
                if (!double.IsFinite(width))
                    continue;
                if (memo.Count < 2048)
                    memo[key] = width;
            }
            widths[address.Column] = Math.Max(widths.GetValueOrDefault(address.Column), width);
        }

        var sizes = sheet.MutableSizes(false);
        foreach (var column in widths.Keys)
            previous[column] = sizes.TryGetValue(column, out double width) ? width : null;
        return Change(() =>
        {
            foreach (var (column, width) in widths)
                sizes[column] = width;
        }, () =>
        {
            foreach (var (column, width) in previous)
                if (width is { } saved)
                    sizes[column] = saved;
                else
                    sizes.Remove(column);
        });
    }

    /// <summary>Merges a range into one cell, or unmerges the merged ranges it touches. Merging clears every cell but the top-left one.</summary>
    /// <param name="range">The range.</param>
    /// <param name="merge">True to merge, false to unmerge.</param>
    /// <returns>The command result.</returns>
    public CommandResult Merge(CellRange range, bool merge = true)
    {
        var sheet = ActiveSheet;
        var affected = sheet.Merges.Where(range.Intersects).ToArray();
        if (merge && (affected.Length > 0 || range.Count == 1))
            return Invalid("Select an unmerged range with more than one cell.");

        var overwritten = merge ? sheet.Cells.Where(cell => cell.Key != range.Start && range.Contains(cell.Key)).ToArray() : [];
        void Apply()
        {
            if (merge)
                sheet.MutableMerges.Add(range);
            foreach (var item in affected)
                sheet.MutableMerges.Remove(item);
            foreach (var item in overwritten)
                sheet.Set(item.Key, default);
        }

        void Revert()
        {
            if (merge)
                sheet.MutableMerges.Remove(range);
            sheet.MutableMerges.AddRange(affected);
            foreach (var item in overwritten)
                sheet.Set(item.Key, item.Value);
        }

        return Change(Apply, Revert);
    }

    /// <summary>Inserts or deletes rows or columns and moves the following cells, sizes and merges.</summary>
    /// <param name="rows">True for rows, false for columns.</param>
    /// <param name="index">The zero-based first row or column.</param>
    /// <param name="count">The number of rows or columns.</param>
    /// <param name="insert">True to insert, false to delete.</param>
    /// <returns>The command result.</returns>
    public CommandResult Shift(bool rows, int index, int count, bool insert)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;

        int limit = rows ? CellAddress.MaxRows : CellAddress.MaxColumns;
        if ((uint)index >= limit || count < 1 || count > limit - index)
            return Invalid("Invalid row or column range.");

        var sheet = ActiveSheet;
        int Coordinate(CellAddress address) => rows ? address.Row : address.Column;
        int Move(int position) => position < index ? position :
            insert ? (position + count < limit ? position + count : -1) :
            (position < index + count ? -1 : position - count);
        CellAddress At(CellAddress address, int position) => rows ? address with { Row = position } : address with { Column = position };

        var prior = sheet.Cells.Where(cell => Coordinate(cell.Key) >= index).ToArray();
        var moved = prior.Select(cell => KeyValuePair.Create(At(cell.Key, Move(Coordinate(cell.Key))), cell.Value))
            .Where(cell => cell.Key.IsValid).ToArray();
        var affectedFormulas = Workbook.Sheets.SelectMany(tab => tab.Cells
            .Where(cell => cell.Value.Formula is not null)
            .Select(cell => (Tab: tab, OldAddress: cell.Key, Old: cell.Value,
                NewAddress: ReferenceEquals(sheet, tab) ? At(cell.Key, Move(Coordinate(cell.Key))) : cell.Key,
                NewFormula: ReferenceAdjustment.Shift(cell.Value.Formula!, tab.Name, sheet.Name, rows, index, count, insert))))
            .Where(item => item.NewAddress.IsValid && item.Old.Formula != item.NewFormula)
            .ToArray();

        var sizes = sheet.MutableSizes(rows);
        var oldSizes = sizes.ToArray();
        var newSizes = oldSizes.Select(item => KeyValuePair.Create(Move(item.Key), item.Value)).Where(item => item.Key >= 0).ToArray();
        var oldMerges = sheet.Merges.ToArray();
        var newMerges = new List<CellRange>();

        foreach (var range in oldMerges)
        {
            int start = Coordinate(range.Start), end = Coordinate(range.End);
            int low = Move(start), high = Move(end);
            if (!insert && end >= index + count && low < 0)
                low = index;
            if (!insert && start < index && high < 0)
                high = index - 1;
            if (insert && low >= 0 && high < 0)
                high = limit - 1;
            if (low >= 0 && high >= 0)
            {
                var shifted = new CellRange(At(range.Start, low), At(range.End, high));
                if (shifted.Count > 1)
                    newMerges.Add(shifted);
            }
        }

        void Apply(KeyValuePair<CellAddress, SheetCell>[] remove, KeyValuePair<CellAddress, SheetCell>[] add,
            KeyValuePair<int, double>[] dimensions, IEnumerable<CellRange> merges)
        {
            foreach (var item in remove)
                sheet.Set(item.Key, default);
            foreach (var item in add)
                sheet.Set(item.Key, item.Value);
            sizes.Clear();
            foreach (var dimension in dimensions)
                sizes.Add(dimension.Key, dimension.Value);
            sheet.MutableMerges.Clear();
            sheet.MutableMerges.AddRange(merges);
        }

        return Change(() =>
        {
            Apply(prior, moved, newSizes, newMerges);
            foreach (var item in affectedFormulas)
                item.Tab.Set(item.NewAddress, item.Old with { Formula = item.NewFormula });
        }, () =>
        {
            Apply(moved, prior, oldSizes, oldMerges);
            foreach (var item in affectedFormulas)
                item.Tab.Set(item.OldAddress, item.Old);
        });
    }
}
