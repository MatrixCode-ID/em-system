// The cell menu follows GridCraft's cell context menu (ui-egui/src/grid/menus.rs @ c0ee120ac2d9b59630f5abf1151dd9b471fe2926), MIT OR Apache-2.0. Copyright (c) 2026 ArtCraft Team and the GridCraft contributors.
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Em.Editing;
using Em.Controls.Grid.Menus;
using Em.Controls.Sheet.Commands;
using Em.Controls.Sheet.Cells;
using Em.Ui.Wpf.Controls;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixSheet
{
    private ContextMenu? cellMenu;
    private (CellRange Source, CellRange Target)? pendingFill;

    /// <summary>Attaches the Material cell menu to the sheet surface; the fill-handle right-drag menu shares it.</summary>
    private void AttachCellMenu(SheetSurface target)
    {
        // Commands are resolved while the menu opens, so a fill command keeps the dragged ranges after the menu closes.
        cellMenu = MenuRenderer.Attach(target, CreateMenu, id => new SheetMenuCommand(this, id, pendingFill));
        cellMenu.Closed += (_, _) =>
        {
            pendingFill = null;
            cellMenu.Placement = PlacementMode.MousePoint;
        };
    }

    private MenuOpeningEventArgs CreateMenu()
    {
        var hit = new MenuHit(MenuRegion.RowCell);
        if (pendingFill is { } fill)
            return new(hit, FillItems(fill.Source));
        EndEdit(true);
        bool full = Mode == EditorMode.Full;
        bool copy = copied is not null && full;
        bool canPaste = full && Clipboard.ContainsText();
        var items = ImmutableArray.CreateBuilder<MenuEntry>();
        items.Add(new MenuCommand("cut", "Cut", "Cut", "Ctrl+X", full));
        items.Add(new MenuCommand("copy", "Copy", "Copy", "Ctrl+C"));
        items.Add(new MenuCommand("paste", "Paste", "Paste", "Ctrl+V", canPaste));
        items.Add(Submenu("Paste Special", "PasteSpecial",
            new MenuCommand("paste-values", "Values", "PasteValues", IsEnabled: copy),
            new MenuCommand("paste-formulas", "Formulas", "Function", IsEnabled: copy),
            new MenuCommand("paste-formats", "Formatting", "PasteFormats", IsEnabled: copy),
            new MenuSeparator(),
            new MenuCommand("paste-transpose", "Transpose", "Transpose", IsEnabled: copy)));
        items.Add(new MenuSeparator());
        items.Add(Submenu("Insert", "InsertCells",
            new MenuCommand("insert-right", "Shift cells right", "ShiftRight", IsEnabled: full),
            new MenuCommand("insert-down", "Shift cells down", "ShiftDown", IsEnabled: full),
            new MenuSeparator(),
            new MenuCommand("insert-row", "Entire row", "EntireRow", IsEnabled: full),
            new MenuCommand("insert-column", "Entire column", "EntireColumn", IsEnabled: full)));
        if (copy && copyMark is not null)
            items.Add(Submenu("Insert Copied Cells", "InsertCopied",
                new MenuCommand("insert-copied-right", "Shift cells right", "ShiftRight"),
                new MenuCommand("insert-copied-down", "Shift cells down", "ShiftDown")));
        items.Add(Submenu("Delete", "DeleteCells",
            new MenuCommand("delete-left", "Shift cells left", "ShiftLeft", IsEnabled: full),
            new MenuCommand("delete-up", "Shift cells up", "ShiftUp", IsEnabled: full),
            new MenuSeparator(),
            new MenuCommand("delete-row", "Entire row", "EntireRow", IsEnabled: full),
            new MenuCommand("delete-column", "Entire column", "EntireColumn", IsEnabled: full)));
        items.Add(new MenuCommand("clear", "Clear Contents", "ClearContents", "Delete", full));
        items.Add(new MenuSeparator());
        items.Add(new MenuCommand("sort-ascending", "Sort A to Z", "SortAscending", IsEnabled: full));
        items.Add(new MenuCommand("sort-descending", "Sort Z to A", "SortDescending", IsEnabled: full));
        items.Add(new MenuSeparator());
        items.Add(new MenuCommand("format", "Format Cells…", "FormatCells", "Ctrl+1", full));
        items.Add(new MenuCommand("pick-list", "Pick From Drop-down List…", "DropDownList", IsEnabled: full));
        items.Add(new MenuCommand("define-name", "Define Name…", "DefineName", IsEnabled: full));
        return new(hit, items.ToImmutable());

        static MenuSubmenu Submenu(string label, string icon, params MenuEntry[] entries) => new(label, [.. entries], icon);
    }

    private ImmutableArray<MenuEntry> FillItems(CellRange source)
    {
        var items = ImmutableArray.CreateBuilder<MenuEntry>();
        items.Add(new MenuCommand("fill-copy", "Copy Cells", "Copy"));
        items.Add(new MenuCommand("fill-series", "Fill Series", "SortAscending"));
        items.Add(new MenuCommand("fill-formats", "Fill Formatting Only", "PasteFormats"));
        items.Add(new MenuCommand("fill-values", "Fill Without Formatting", "PasteValues"));
        if (Session.StartsWithDate(source))
        {
            items.Add(new MenuSeparator());
            items.Add(new MenuCommand("fill-days", "Fill Days", "Calendar"));
            items.Add(new MenuCommand("fill-weekdays", "Fill Weekdays", "Calendar"));
            items.Add(new MenuCommand("fill-months", "Fill Months", "Calendar"));
            items.Add(new MenuCommand("fill-years", "Fill Years", "Calendar"));
        }
        return items.ToImmutable();
    }

    /// <summary>Opens the shared menu with the fill choices for a right-drag of the fill handle.</summary>
    private void OpenFillMenu(CellRange source, CellRange target)
    {
        if (cellMenu is null)
            return;
        pendingFill = (source, target);
        cellMenu.Placement = PlacementMode.MousePoint;
        cellMenu.IsOpen = true;
    }

    private bool CanRunMenu(string id) => id switch
    {
        "copy" => true,
        "paste" => Mode == EditorMode.Full && Clipboard.ContainsText(),
        _ when id.StartsWith("paste-", StringComparison.Ordinal) || id.StartsWith("insert-copied", StringComparison.Ordinal) =>
            Mode == EditorMode.Full && copied is not null,
        _ => Mode == EditorMode.Full
    };

    private void RunMenu(string id, (CellRange Source, CellRange Target)? fillRequest)
    {
        var selection = Session.Selection;
        var active = Session.ActiveCell;
        CommandResult? result = null;
        switch (id)
        {
            case "cut" or "copy" or "paste" or "clear" or "format" or "insert-row" or "delete-row" or "insert-column" or "delete-column":
                ExecuteCommand(id);
                return;
            case "paste-values": PasteSpecial(SheetPastePart.Values, false); return;
            case "paste-formulas": PasteSpecial(SheetPastePart.Formulas, false); return;
            case "paste-formats": PasteSpecial(SheetPastePart.Formats, false); return;
            case "paste-transpose": PasteSpecial(SheetPastePart.All, true); return;
            case "insert-right": result = Session.ShiftCells(selection, false, true); break;
            case "insert-down": result = Session.ShiftCells(selection, true, true); break;
            case "delete-left": result = Session.ShiftCells(selection, false, false); break;
            case "delete-up": result = Session.ShiftCells(selection, true, false); break;
            case "insert-copied-right" or "insert-copied-down" when copied is { } clip:
            {
                var end = new CellAddress(selection.Start.Row + clip.Rows - 1, selection.Start.Column + clip.Columns - 1);
                if (!end.IsValid)
                {
                    ShowStatus("The copied cells do not fit here.");
                    return;
                }
                result = Session.ShiftCells(new CellRange(selection.Start, end), id.EndsWith("down", StringComparison.Ordinal), true);
                if (result.Value.Accepted)
                {
                    result = Session.Paste(selection.Start, clip);
                    SelectAndReveal(selection.Start, end);
                }
                break;
            }
            case "sort-ascending" or "sort-descending":
            {
                var range = selection.Count == 1 ? Session.CurrentRegion(active) : selection;
                if (range.Height < 2)
                    return;
                result = Session.Sort(range, active.Column, id == "sort-descending");
                if (result.Value.Accepted && selection.Count == 1)
                    SelectAndReveal(active, active);
                break;
            }
            case "pick-list": PickFromList(active); return;
            case "define-name": DefineName(selection); return;
            default:
                if (fillRequest is { } fill && FillMode(id) is { } mode)
                    FillFromHandle(fill.Source, fill.Target, mode);
                return;
        }
        if (result is { Accepted: false } rejected)
            ShowStatus(rejected.Reason);
    }

    private static SheetFillMode? FillMode(string id) => id switch
    {
        "fill-copy" => SheetFillMode.Copy,
        "fill-series" => SheetFillMode.Extend,
        "fill-formats" => SheetFillMode.Formats,
        "fill-values" => SheetFillMode.ValuesOnly,
        "fill-days" => SheetFillMode.Days,
        "fill-weekdays" => SheetFillMode.Weekdays,
        "fill-months" => SheetFillMode.Months,
        "fill-years" => SheetFillMode.Years,
        _ => null
    };

    private void PasteSpecial(SheetPastePart part, bool transpose)
    {
        if (copied is not { } clip)
            return;
        var result = Session.PasteSpecial(Session.Selection.Start, clip, part, transpose);
        if (!result.Accepted)
            ShowStatus(result.Reason);
    }

    private void PickFromList(CellAddress cell)
    {
        var values = Session.ListValues(cell);
        if (values.Count == 0)
        {
            ShowStatus("There are no entries above or below this cell to pick from.");
            return;
        }
        var list = new ListView
        {
            ItemsSource = values, Height = Math.Min(320, 36 * values.Count + 4), SelectedIndex = 0, BorderThickness = new Thickness(0),
            ItemContainerStyle = Material.Style("ListViewItem")
        };
        Material.Paint(list, Control.BackgroundProperty, "SurfaceContainerHigh");
        var form = new StackPanel();
        form.Children.Add(Caption($"Entries in column {CellAddress.ColumnName(cell.Column)}"));
        form.Children.Add(list);
        var (window, ok) = DialogWindow("Pick from list", form);
        void Pick()
        {
            if (list.SelectedItem is string value)
            {
                Session.SetCell(cell, value);
                window.DialogResult = true;
            }
        }
        ok.Click += (_, _) => Pick();
        list.MouseDoubleClick += (_, _) => Pick();
        list.Loaded += (_, _) => (list.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.Focus();
        window.ShowDialog();
    }

    private void DefineName(CellRange selection)
    {
        string sheet = Session.ActiveSheet.Name;
        string quoted = sheet.All(c => char.IsLetterOrDigit(c) || c == '_') ? sheet : "'" + sheet.Replace("'", "''") + "'";
        string Absolute(CellAddress address) => "$" + CellAddress.ColumnName(address.Column) + "$" + (address.Row + 1);
        var name = new TextBox { Style = Material.Style("Field") };
        var refers = new TextBox
        {
            Style = Material.Style("Field"),
            Text = "=" + quoted + "!" + Absolute(selection.Start) + (selection.Count > 1 ? ":" + Absolute(selection.End) : "")
        };
        if (Session.GetValue(Session.ActiveSheet, selection.Start) is { Kind: CellValueKind.Text, Text: { } label })
            name.Text = new string(label.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        var form = new StackPanel();
        form.Children.Add(Caption("Name"));
        form.Children.Add(name);
        form.Children.Add(Caption("Refers to"));
        form.Children.Add(refers);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        Material.Paint(error, TextBlock.ForegroundProperty, "Error");
        form.Children.Add(error);
        var (window, ok) = DialogWindow("Define name", form);
        ok.Click += (_, _) =>
        {
            var result = Session.DefineName(name.Text, refers.Text);
            if (result.Accepted)
                window.DialogResult = true;
            else
            {
                error.Text = result.Reason;
                error.Visibility = Visibility.Visible;
            }
        };
        name.Loaded += (_, _) => { name.Focus(); name.SelectAll(); };
        window.ShowDialog();
    }

    private static TextBlock Caption(string text)
    {
        var caption = new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 4), FontSize = 12 };
        Material.Paint(caption, TextBlock.ForegroundProperty, "OnSurfaceVariant");
        return caption;
    }

    /// <summary>A Material dialog with Cancel and OK actions under the form.</summary>
    private (Window Window, Button Ok) DialogWindow(string title, StackPanel form)
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = Material.Button("Cancel", "TextButton");
        var ok = Material.Button("OK", "FilledButton");
        cancel.IsCancel = true;
        ok.IsDefault = true;
        ok.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(cancel);
        actions.Children.Add(ok);
        form.Children.Add(actions);
        return (Material.Dialog(this, title, form, 360), ok);
    }

    private sealed class SheetMenuCommand(MatrixSheet sheet, string id, (CellRange Source, CellRange Target)? fill) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => sheet.CanRunMenu(id);
        public void Execute(object? parameter) => sheet.RunMenu(id, fill);
    }
}
