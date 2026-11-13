// Ported from GridCraft (engine/src/cmd/edit.rs @ 01abd9542076101ffff80e98ef4e502596bfce50), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using Em.Editing;
using Em.Controls.Sheet.Calc;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.Model;

namespace Em.Controls.Sheet.Commands;

public sealed partial class SheetSession
{
    private readonly UndoHistory history = new();
    private readonly Dictionary<SheetTab, CellRange> selections = new();
    private readonly Dictionary<SheetTab, (CellAddress Anchor, CellAddress End)> selectionEnds = new();
    private EditorMode mode;
    private SheetCalculation? calculation;

    /// <summary>Starts an editing session over a workbook, with the first visible sheet active.</summary>
    /// <param name="workbook">The workbook to edit, or null for a new workbook with one sheet.</param>
    public SheetSession(SheetWorkbook? workbook = null)
    {
        Workbook = workbook ?? new();
        ActiveSheet = Workbook.Sheets.First(sheet => !sheet.IsHidden);
    }

    /// <summary>Gets the edited workbook.</summary>
    public SheetWorkbook Workbook { get; }
    /// <summary>Gets the sheet the commands act on.</summary>
    public SheetTab ActiveSheet { get; private set; }
    /// <summary>Gets the selected range on the active sheet.</summary>
    public CellRange Selection => selections.GetValueOrDefault(ActiveSheet);

    /// <summary>The cell the selection was started from. It is the active cell, which editing and the formula bar use.</summary>
    public CellAddress ActiveCell => Ends().Anchor;

    /// <summary>The moving end of the selection, which Shift with an arrow key or a drag extends.</summary>
    public CellAddress SelectionEnd => Ends().End;
    /// <summary>Gets whether a change can be undone; always false in viewer mode.</summary>
    public bool CanUndo => Mode == EditorMode.Full && history.CanUndo;
    /// <summary>Gets whether an undone change can be redone; always false in viewer mode.</summary>
    public bool CanRedo => Mode == EditorMode.Full && history.CanRedo;

    /// <summary>Gets or sets whether the session allows changes. Commands that change the workbook are rejected in viewer mode.</summary>
    public EditorMode Mode
    {
        get => mode;
        set
        {
            if (mode == value)
                return;
            mode = value;
            ModeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Occurs when <see cref="Mode"/> changes.</summary>
    public event EventHandler? ModeChanged;
    /// <summary>Occurs after a command changes the workbook, including undo and redo.</summary>
    public event EventHandler? DocumentChanged;

    /// <summary>Makes a visible sheet of the workbook active.</summary>
    /// <param name="sheet">The sheet to activate.</param>
    /// <returns>False when the sheet is hidden or not in the workbook.</returns>
    public bool ActivateSheet(SheetTab sheet)
    {
        if (!Workbook.Sheets.Contains(sheet) || sheet.IsHidden)
            return false;
        ActiveSheet = sheet;
        return true;
    }

    /// <summary>Returns the value of a cell, calculating formulas on demand.</summary>
    /// <param name="sheet">The sheet.</param>
    /// <param name="address">The cell.</param>
    /// <returns>The constant value or the formula result.</returns>
    public CellValue GetValue(SheetTab sheet, CellAddress address) =>
        (calculation ??= new(Workbook)).GetValue(sheet, address);

    /// <summary>Selects a range on the active sheet, with its top-left cell as the active cell.</summary>
    /// <param name="range">The range to select.</param>
    public void Select(CellRange range)
    {
        selections[ActiveSheet] = range;
        selectionEnds[ActiveSheet] = (range.Start, range.End);
    }

    /// <summary>Selects the range between an anchor (the active cell) and a moving end, in any direction.</summary>
    public void Select(CellAddress anchor, CellAddress end)
    {
        selections[ActiveSheet] = new(anchor, end);
        selectionEnds[ActiveSheet] = (anchor, end);
    }

    private (CellAddress Anchor, CellAddress End) Ends()
    {
        var range = Selection;
        return selectionEnds.TryGetValue(ActiveSheet, out var ends) && range.Contains(ends.Anchor) && range.Contains(ends.End)
            ? ends : (range.Start, range.End);
    }

    /// <summary>Sets a cell from typed input; text that starts with an equals sign becomes a formula. The cell keeps its style.</summary>
    /// <param name="address">The cell.</param>
    /// <param name="input">The typed text.</param>
    /// <returns>The command result.</returns>
    public CommandResult SetCell(CellAddress address, string input)
    {
        if (!address.IsValid)
            return Invalid("Invalid cell address.");
        return ChangeCells([(address, SheetCell.FromInput(input, ActiveSheet[address].StyleIndex))]);
    }

    /// <summary>Clears the values and formulas in a range and keeps the cell styles.</summary>
    /// <param name="range">The range to clear.</param>
    /// <returns>The command result.</returns>
    public CommandResult Clear(CellRange range) => ChangeCells(ActiveSheet.Cells
        .Where(pair => range.Contains(pair.Key))
        .Select(pair => (pair.Key, new SheetCell(default, StyleIndex: pair.Value.StyleIndex))));

    /// <summary>Changes the style of every cell in a range, up to one million cells.</summary>
    /// <param name="range">The range to format.</param>
    /// <param name="format">Returns the new style for the current style of a cell.</param>
    /// <returns>The command result.</returns>
    public CommandResult Format(CellRange range, Func<SheetStyle, SheetStyle> format)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (range.Count > 1_000_000)
            return Invalid("Formatting is limited to one million cells per command.");

        return ChangeCells(range.Cells().Select(address =>
        {
            var cell = ActiveSheet[address];
            return (address, cell with { StyleIndex = Workbook.Intern(format(Workbook.Styles[cell.StyleIndex])) });
        }));
    }

    /// <summary>Reverts the last change.</summary>
    /// <returns>The command result.</returns>
    public CommandResult Undo()
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (!history.Undo())
            return Invalid("There is no change to undo.");
        NotifyChanged();
        return CommandResult.Success;
    }

    /// <summary>Reapplies the last undone change.</summary>
    /// <returns>The command result.</returns>
    public CommandResult Redo()
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;
        if (!history.Redo())
            return Invalid("There is no change to redo.");
        NotifyChanged();
        return CommandResult.Success;
    }

    private CommandResult ChangeCells(IEnumerable<(CellAddress Address, SheetCell Value)> changes)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;

        var sheet = ActiveSheet;
        var cells = changes.Select(change => (change.Address, Before: sheet[change.Address], After: change.Value))
            .Where(change => change.Before != change.After).ToArray();
        if (cells.Length == 0)
            return CommandResult.Success;

        void Apply(bool forward)
        {
            foreach (var change in cells)
                sheet.Set(change.Address, forward ? change.After : change.Before);
        }

        return Change(() => Apply(true), () => Apply(false));
    }

    private CommandResult Change(Action apply, Action revert)
    {
        if (Mode == EditorMode.Viewer)
            return ReadOnly;

        apply();
        history.Record(revert, apply);
        NotifyChanged();
        return CommandResult.Success;
    }

    private void NotifyChanged()
    {
        calculation = null;
        if (ActiveSheet.IsHidden || !Workbook.Sheets.Contains(ActiveSheet))
            ActiveSheet = Workbook.Sheets.First(sheet => !sheet.IsHidden);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static CommandResult Invalid(string reason) => CommandResult.Rejected(reason);
    private static CommandResult ReadOnly => Invalid("The workbook is read-only.");
}
