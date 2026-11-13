using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Em.Documents.Formats;
using Em.Documents.Model;
using Em.Documents.Pdf;
using Em.Editing;
using Em.Controls.Sheet.Calc;
using Em.Controls.Sheet.Commands;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.IO;
using Em.Controls.Sheet.Model;
using Em.Controls.Sheet.NumberFormats;
using Em.Ui.Wpf.Controls;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Controls;

/// <summary>A spreadsheet editor for SheetWorkbook workbooks, with formulas, formatting, merged cells, frozen panes, CSV and XLSX files, and a compact toolbar or a ribbon.</summary>
public sealed partial class MatrixSheet
{
    /// <inheritdoc/>
    protected override bool IsCommandEnabled(string id) => id switch
    {
        "undo" => Session.CanUndo,
        "redo" => Session.CanRedo,
        "paste-values" or "paste-formulas" or "paste-formats" or "paste-transpose" => copied is not null,
        "unmerge" => Session.ActiveSheet.Merges.Any(Session.Selection.Intersects),
        "unhide-sheet" => Session.Workbook.Sheets.Any(sheet => sheet.IsHidden),
        "unfreeze" => Session.ActiveSheet.FrozenRows > 0 || Session.ActiveSheet.FrozenColumns > 0,
        _ => true
    };

    /// <inheritdoc/>
    protected override void RunCommand(string id)
    {
        var selection = Session.Selection;
        switch (id)
        {
            case "open":
                var dialog = new OpenFileDialog { Filter = "Spreadsheet files|*.xlsx;*.csv|Excel workbook|*.xlsx|CSV file|*.csv" };
                if (dialog.ShowDialog(Window.GetWindow(this)) == true)
                    Source = dialog.FileName;
                break;
            case "save": Save(false); break;
            case "export": Save(true); break;
            case "print":
                var formats = new DocumentFormatRegistry();
                formats.Register(new PdfDocumentWriter());
                var preview = new PrintPreviewWindow((setup, fit, _) => Snapshot(setup, fit), formats,
                    dark: GridTheme.GetMode(this) == GridThemeMode.Dark) { Owner = Window.GetWindow(this) };
                preview.Show();
                break;
            case "copy":
            case "cut":
                if (selection.Count > 1_000_000)
                    break;
                copied = Session.Copy(selection);
                var data = new DataObject(DataFormats.UnicodeText, copied.Text);
                data.SetData(DataFormats.Html, copied.Html);
                Clipboard.SetDataObject(data);
                if (id == "cut") KeepingCopyMark(() => Session.Clear(selection));
                SetCopyMark(selection, id == "cut");
                break;
            case "paste":
                if (Clipboard.ContainsText())
                {
                    string text = Clipboard.GetText();
                    if (copied?.Text == text)
                    {
                        // Pasting a copy keeps the marquee for further pastes; pasting a cut ends it.
                        bool cut = copyMark?.Cut == true;
                        KeepingCopyMark(() => Session.Paste(selection.Start, copied));
                        if (cut) ClearCopyMark();
                    }
                    else
                    {
                        ClearCopyMark();
                        Session.Paste(selection.Start, text);
                    }
                }
                break;
            case "undo": Session.Undo(); break;
            case "redo": Session.Redo(); break;
            case "clear": Session.Clear(selection); break;
            case "add-sheet":
                int index = Session.Workbook.Sheets.Count + 1;
                while (!Session.AddSheet("Sheet" + index++).Accepted) { }
                ScrollTo(0, 0);
                break;
            case "insert-row": Session.Shift(true, selection.Start.Row, selection.Height, true); break;
            case "delete-row": Session.Shift(true, selection.Start.Row, selection.Height, false); break;
            case "insert-column": Session.Shift(false, selection.Start.Column, selection.Width, true); break;
            case "delete-column": Session.Shift(false, selection.Start.Column, selection.Width, false); break;
            case "freeze": Session.Freeze(selection.Start.Row, selection.Start.Column); break;
            case "freeze-top-row": Session.Freeze(1, 0); break;
            case "freeze-first-column": Session.Freeze(0, 1); break;
            case "unfreeze": Session.Freeze(0, 0); break;
            case "merge":
                if (Session.ActiveSheet.Merges.Any(selection.Intersects))
                    Report(Session.Merge(selection, false));
                else if (Report(Session.Merge(selection)))
                    Session.Format(selection, style => style with { Alignment = CellAlignment.Center });
                break;
            case "merge-across": Report(Session.MergeAcross(selection)); break;
            case "merge-cells": Report(Session.Merge(selection)); break;
            case "unmerge": Report(Session.Merge(selection, false)); break;
            case "function": BeginEdit(Session.ActiveCell, "="); break;
            case "autosum" or "sum": AutoSum("SUM"); break;
            case "average": AutoSum("AVERAGE"); break;
            case "count": AutoSum("COUNT"); break;
            case "max": AutoSum("MAX"); break;
            case "min": AutoSum("MIN"); break;
            case "paste-values" or "paste-formulas" or "paste-formats" or "paste-transpose" or "sort-ascending" or "sort-descending" or
                "define-name" or "pick-list":
                RunMenu(id, null);
                break;
            case "insert-cells-right": Report(Session.ShiftCells(selection, false, true)); break;
            case "insert-cells-down": Report(Session.ShiftCells(selection, true, true)); break;
            case "delete-cells-left": Report(Session.ShiftCells(selection, false, false)); break;
            case "delete-cells-up": Report(Session.ShiftCells(selection, true, false)); break;
            case "delete-sheet": Report(Session.DeleteSheet(Session.ActiveSheet)); break;
            case "rename-sheet":
                if (AskText("Rename sheet", "Name", Session.ActiveSheet.Name) is { } name)
                    Report(Session.RenameSheet(Session.ActiveSheet, name));
                break;
            case "hide-sheet": Report(Session.HideSheet(Session.ActiveSheet, true)); break;
            case "unhide-sheet":
                var hidden = Session.Workbook.Sheets.Where(sheet => sheet.IsHidden).ToArray();
                if (hidden.Length > 0 && AskText("Unhide sheet", "Sheet (" + string.Join(", ", hidden.Select(sheet => sheet.Name)) + ")",
                    hidden[0].Name) is { } shown && hidden.FirstOrDefault(sheet => sheet.Name == shown) is { } target &&
                    Report(Session.HideSheet(target, false)))
                    Session.ActivateSheet(target);
                break;
            case "row-height" or "column-width":
            {
                bool rows = id == "row-height";
                double current = rows ? Session.ActiveSheet.RowHeights.GetValueOrDefault(selection.Start.Row, 28) :
                    Session.ActiveSheet.ColumnWidths.GetValueOrDefault(selection.Start.Column, 96);
                if (AskText(rows ? "Row height" : "Column width", "Size (DIP)", current.ToString("0.##", CultureInfo.InvariantCulture)) is { } text &&
                    double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double size) && size is >= 0 and <= 2000)
                {
                    int first = rows ? selection.Start.Row : selection.Start.Column, count = rows ? selection.Height : selection.Width;
                    for (int line = first; line < first + Math.Min(count, 1000); line++)
                        Session.Resize(rows, line, size);
                }
                break;
            }
            case "autofit-column": surface?.AutoFit(selection); break;
            case "fill-down": FillEdge(selection, new(selection.Start, new(selection.Start.Row, selection.End.Column))); break;
            case "fill-up": FillEdge(selection, new(new(selection.End.Row, selection.Start.Column), selection.End)); break;
            case "fill-right": FillEdge(selection, new(selection.Start, new(selection.End.Row, selection.Start.Column))); break;
            case "fill-left": FillEdge(selection, new(new(selection.Start.Row, selection.End.Column), selection.End)); break;
            case "clear-all": Session.ClearAll(selection); break;
            case "clear-formats": Session.ClearFormats(selection); break;
            case "replace":
                ShowFind();
                findReplacement?.Focus();
                break;
            case "zoom-100": SetZoom(1); break;
            case "zoom-selection": ZoomToSelection(); break;
            case "find": ShowFind(); break;
            case "go":
                nameBox?.Focus();
                nameBox?.SelectAll();
                break;
            case "select-all":
                Session.Select(new(default, new(CellAddress.MaxRows - 1, CellAddress.MaxColumns - 1)));
                Refresh();
                break;
            case "zoom-in": SetZoom(Math.Min(MaxZoom, zoom + .1)); break;
            case "zoom-out": SetZoom(Math.Max(MinZoom, zoom - .1)); break;
            default: Format(id, selection); break;
        }
    }

    private void Format(string id, CellRange range)
    {
        var current = Session.Workbook.Styles[Session.ActiveSheet[range.Start].StyleIndex];
        switch (id)
        {
            case "bold": Session.Format(range, style => style with { Bold = !current.Bold }); return;
            case "italic": Session.Format(range, style => style with { Italic = !current.Italic }); return;
            case "underline": Session.Format(range, style => style with { Underline = !current.Underline }); return;
            case "strike": Session.Format(range, style => style with { Strike = !current.Strike }); return;
            case "wrap": Session.Format(range, style => style with { Wrap = !current.Wrap }); return;
            case "left": Session.Format(range, style => style with { Alignment = CellAlignment.Start }); return;
            case "center": Session.Format(range, style => style with { Alignment = CellAlignment.Center }); return;
            case "right": Session.Format(range, style => style with { Alignment = CellAlignment.End }); return;
            case "percent": Session.Format(range, style => style with { NumberFormat = "0%" }); return;
            case "thousands": Session.Format(range, style => style with { NumberFormat = "#,##0.00" }); return;
            case "accounting": Session.Format(range, style => style with { NumberFormat = "$#,##0.00;($#,##0.00)" }); return;
            case "decimal-add" or "decimal-remove":
            {
                string next = SheetNumberFormatSteps.Decimals(current.NumberFormat, id == "decimal-add" ? 1 : -1,
                    Session.GetValue(Session.ActiveSheet, range.Start));
                Session.Format(range, style => style with { NumberFormat = next });
                return;
            }
            case "grow-font" or "shrink-font":
            {
                double points = Math.Round(current.FontSize * 3 / 4 * 2) / 2;
                double next = id == "grow-font" ? FontSizes.FirstOrDefault(size => size > points, Math.Min(points + 10, 409)) :
                    FontSizes.LastOrDefault(size => size < points, Math.Max(1, points - 1));
                Session.Format(range, style => style with { FontSize = next * 4 / 3 });
                return;
            }
            case "align-top": Session.Format(range, style => style with { VerticalAlignment = CellVerticalAlignment.Top }); return;
            case "align-middle": Session.Format(range, style => style with { VerticalAlignment = CellVerticalAlignment.Center }); return;
            case "align-bottom": Session.Format(range, style => style with { VerticalAlignment = CellVerticalAlignment.Bottom }); return;
            case "indent-in": Session.Format(range, style => style with { Indent = Math.Min(15, style.Indent + 1) }); return;
            case "indent-out": Session.Format(range, style => style with { Indent = Math.Max(0, style.Indent - 1) }); return;
            case "borders" or "border-all": Session.Format(range, style => style with { Borders = CellBorders.All }); return;
            case "border-none": Session.Format(range, style => style with { Borders = CellBorders.None }); return;
            case "border-top": Session.FormatEach(range, (cell, style) => cell.Row == range.Start.Row ? style with { Borders = style.Borders | CellBorders.Top } : style); return;
            case "border-bottom": Session.FormatEach(range, (cell, style) => cell.Row == range.End.Row ? style with { Borders = style.Borders | CellBorders.Bottom } : style); return;
            case "border-left": Session.FormatEach(range, (cell, style) => cell.Column == range.Start.Column ? style with { Borders = style.Borders | CellBorders.Left } : style); return;
            case "border-right": Session.FormatEach(range, (cell, style) => cell.Column == range.End.Column ? style with { Borders = style.Borders | CellBorders.Right } : style); return;
            case "border-outside":
                Session.FormatEach(range, (cell, style) => style with
                {
                    Borders = style.Borders | (cell.Row == range.Start.Row ? CellBorders.Top : 0) | (cell.Row == range.End.Row ? CellBorders.Bottom : 0) |
                        (cell.Column == range.Start.Column ? CellBorders.Left : 0) | (cell.Column == range.End.Column ? CellBorders.Right : 0)
                });
                return;
            case "border-inside":
                Session.FormatEach(range, (cell, style) => style with
                {
                    Borders = style.Borders | (cell.Row < range.End.Row ? CellBorders.Bottom : 0) | (cell.Column < range.End.Column ? CellBorders.Right : 0)
                });
                return;
        }
        if (id is not ("format" or "font" or "font-size" or "font-color" or "fill" or "number-format"))
            return;
        var number = Field(double.NaN); number.Text = current.NumberFormat;
        var family = Field(double.NaN); family.Text = current.FontFamily;
        var size = Field(double.NaN); size.Text = current.FontSize.ToString(CultureInfo.InvariantCulture);
        var text = Field(double.NaN); text.Text = current.TextColor?.ToString("X8", CultureInfo.InvariantCulture) ?? "";
        var fill = Field(double.NaN); fill.Text = current.FillColor?.ToString("X8", CultureInfo.InvariantCulture) ?? "";
        var border = Field(double.NaN); border.Text = current.BorderColor?.ToString("X8", CultureInfo.InvariantCulture) ?? "";
        var alignment = new ComboBox { ItemsSource = Enum.GetValues<CellAlignment>(), SelectedItem = current.Alignment };
        var dialogTabs = new TabControl();
        Add("Number", ("Format code", number));
        Add("Alignment", ("Horizontal", alignment));
        Add("Font", ("Family", family), ("Size", size), ("Text color (ARGB)", text));
        Add("Border", ("Border color (ARGB)", border));
        Add("Fill", ("Fill color (ARGB)", fill));
        dialogTabs.SelectedIndex = id switch { "font" or "font-size" or "font-color" => 2, "fill" => 4, _ => 0 };
        var form = new StackPanel();
        form.Children.Add(dialogTabs);
        var apply = Material.Button("Apply", "TextButton");
        form.Children.Add(apply);
        var window = Material.Dialog(this, "Format cells", form, 440);
        apply.Click += (_, _) =>
        {
            if (!double.TryParse(size.Text, CultureInfo.InvariantCulture, out double pixels) || !double.IsFinite(pixels) || pixels is < 1 or > 240)
                return;
            Session.Format(range, style => style with
            {
                FontFamily = string.IsNullOrWhiteSpace(family.Text) ? "Roboto" : family.Text,
                FontSize = pixels, Alignment = (CellAlignment)alignment.SelectedItem,
                NumberFormat = string.IsNullOrWhiteSpace(number.Text) ? "General" : number.Text,
                TextColor = ParseColor(text.Text), FillColor = ParseColor(fill.Text), BorderColor = ParseColor(border.Text)
            });
            window.DialogResult = true;
        };
        window.ShowDialog();

        void Add(string label, params (string Label, FrameworkElement Field)[] fields)
        {
            var panel = new StackPanel { Margin = new Thickness(16) };
            foreach (var item in fields)
            {
                panel.Children.Add(new TextBlock { Text = item.Label, Margin = new Thickness(0, 8, 0, 4) });
                panel.Children.Add(item.Field);
            }
            dialogTabs.Items.Add(new TabItem { Header = label, Content = panel });
        }

        static uint? ParseColor(string value) => uint.TryParse(value.TrimStart('#'), NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out uint parsed) ? parsed : null;
    }

    private bool Report(CommandResult result)
    {
        if (!result.Accepted)
            ShowStatus(result.Reason);
        return result.Accepted;
    }

    private string? AskText(string title, string label, string value)
    {
        var input = new TextBox { Style = Material.Style("Field"), Text = value };
        var form = new StackPanel();
        form.Children.Add(Caption(label));
        form.Children.Add(input);
        var (window, ok) = DialogWindow(title, form);
        ok.Click += (_, _) => window.DialogResult = true;
        input.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
        return window.ShowDialog() == true ? input.Text.Trim() : null;
    }

    /// <summary>Copies the edge row or column of the selection over the rest of it, like Excel's Fill Down, Right, Up and Left.</summary>
    private void FillEdge(CellRange selection, CellRange source)
    {
        if (selection.Count == 1)
        {
            // A single cell takes the cell above (Down) or to the left (Right), as Excel does.
            var cell = selection.Start;
            source = source.Start.Row == cell.Row && source.End.Column == cell.Column && source.Start.Column == cell.Column
                ? new(new(cell.Row, Math.Max(0, cell.Column - 1)), new(cell.Row, Math.Max(0, cell.Column - 1)))
                : new(new(Math.Max(0, cell.Row - 1), cell.Column), new(Math.Max(0, cell.Row - 1), cell.Column));
            if (source.Start == cell)
                return;
            selection = new(new(Math.Min(source.Start.Row, cell.Row), Math.Min(source.Start.Column, cell.Column)), cell);
        }
        Report(Session.Fill(source, selection, SheetFillMode.Copy));
    }

    /// <summary>Enters SUM (or another aggregate) for the selection, as Excel's AutoSum does.</summary>
    private void AutoSum(string function)
    {
        var selection = Session.Selection;
        var sheet = Session.ActiveSheet;
        bool Numeric(CellAddress address) => Session.GetValue(sheet, address).Kind == CellValueKind.Number;
        if (selection.Count == 1)
        {
            var cell = selection.Start;
            int top = cell.Row;
            while (top > 0 && Numeric(new(top - 1, cell.Column)))
                top--;
            if (top < cell.Row)
            {
                BeginEdit(cell, $"={function}({new CellRange(new(top, cell.Column), new(cell.Row - 1, cell.Column))})");
                return;
            }
            int left = cell.Column;
            while (left > 0 && Numeric(new(cell.Row, left - 1)))
                left--;
            BeginEdit(cell, left < cell.Column ? $"={function}({new CellRange(new(cell.Row, left), new(cell.Row, cell.Column - 1))})" : $"={function}(");
            return;
        }
        if (selection.Width > 16_384 || selection.End.Row + 1 >= CellAddress.MaxRows)
            return;
        // A range gets a total below each of its columns.
        Report(Session.SetCells(Enumerable.Range(selection.Start.Column, selection.Width).Select(column =>
            (new CellAddress(selection.End.Row + 1, column),
                $"={function}({new CellRange(new(selection.Start.Row, column), new(selection.End.Row, column))})"))));
    }

    private void ZoomToSelection()
    {
        if (surface is null || surface.ActualWidth <= 0)
            return;
        var selection = Session.Selection;
        double width = Enumerable.Range(selection.Start.Column, Math.Min(selection.Width, 500))
            .Sum(column => Session.ActiveSheet.ColumnWidths.GetValueOrDefault(column, 96));
        double height = Enumerable.Range(selection.Start.Row, Math.Min(selection.Height, 2000))
            .Sum(row => Session.ActiveSheet.RowHeights.GetValueOrDefault(row, 28));
        SetZoom(Math.Clamp(Math.Min((surface.ActualWidth - 60) / width, (surface.ActualHeight - 40) / height), MinZoom, MaxZoom));
        SelectAndReveal(selection.Start, selection.End);
    }

    private void ShowFind()
    {
        if (body is null)
            return;
        if (search is not null)
        {
            search.Focus();
            return;
        }
        search = Field(180);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Height = 44 };
        Material.Paint(panel, Panel.BackgroundProperty, "SurfaceContainerHigh");
        var query = search;
        var replace = Field(180);
        var next = Material.Button("Find next", "TextButton");
        var change = Material.Button("Replace all", "TextButton");
        var close = Material.IconButton("Close", "Close Find");
        panel.Children.Add(query);
        panel.Children.Add(next);
        findReplacement = replace;
        replaceAction = change;
        replace.Visibility = change.Visibility = Mode == EditorMode.Full ? Visibility.Visible : Visibility.Collapsed;
        panel.Children.Add(replace);
        panel.Children.Add(change);
        panel.Children.Add(close);
        DockPanel.SetDock(panel, Dock.Top);
        body.Children.Insert(1, panel);
        next.Click += (_, _) =>
        {
            if (query.Text.Length == 0)
                return;
            var selected = Session.ActiveCell;
            var matches = Session.ActiveSheet.Cells.Keys.Where(address =>
                    Session.GetValue(Session.ActiveSheet, address).ToString().Contains(query.Text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(address => address.Row).ThenBy(address => address.Column).ToArray();
            if (matches.Length == 0)
                return;
            var found = matches.FirstOrDefault(address => address.Row > selected.Row ||
                address.Row == selected.Row && address.Column > selected.Column, matches[0]);
            SelectAndReveal(new(found, found));
        };
        change.Click += (_, _) =>
        {
            if (query.Text.Length == 0 || Mode != EditorMode.Full)
                return;
            var changes = Session.ActiveSheet.Cells.Where(pair => pair.Value.Formula is null &&
                pair.Value.Input.Contains(query.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            foreach (var (address, cell) in changes)
                Session.SetCell(address, cell.Input.Replace(query.Text, replace.Text, StringComparison.OrdinalIgnoreCase));
        };
        close.Click += (_, _) =>
        {
            body.Children.Remove(panel);
            search = null;
            findReplacement = replaceAction = null;
            surface?.Focus();
        };
        query.Focus();
    }

    private void Save(bool export)
    {
        var dialog = new SaveFileDialog
        {
            Filter = export ? "Excel workbook|*.xlsx|CSV file|*.csv|PDF document|*.pdf" : "Excel workbook|*.xlsx|CSV file|*.csv",
            FileName = Session.ActiveSheet.Name
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        _ = WriteFile(dialog.FileName);
    }

    private async Task WriteFile(string path)
    {
        try
        {
            using var stream = File.Create(path);
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".csv": SheetFiles.SaveCsv(Session.Workbook, Session.ActiveSheet, stream); break;
                case ".pdf": await new PdfDocumentWriter().WriteAsync(Snapshot(new(), true), stream); break;
                default: SheetFiles.SaveXlsx(Session.Workbook, stream); break;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (status is not null)
                status.Text = error.Message;
        }
    }

    private Document Snapshot(PageSetup page, bool fit)
    {
        var workbook = Session.Workbook;
        var sheet = Session.ActiveSheet;
        int lastColumn = sheet.Cells.Keys.Select(cell => cell.Column).DefaultIfEmpty(0).Max();
        var rows = sheet.Cells.GroupBy(pair => pair.Key.Row).OrderBy(group => group.Key)
            .Select(group => group.Select(item => (item.Key.Column, Text: Session.GetValue(sheet, item.Key).ToString())).ToArray())
            .ToArray();
        var columns = Enumerable.Range(0, lastColumn + 1)
            .Select(column => new DocumentColumn(CellAddress.ColumnName(column), sheet.ColumnWidths.GetValueOrDefault(column, 96)))
            .ToImmutableArray();
        return new(sheet.Name, [new(page, [new DocumentTable(columns, ReadRows, SheetName: sheet.Name)])]) { FitToWidth = fit };

        async IAsyncEnumerable<DocumentRow> ReadRows([EnumeratorCancellation] CancellationToken token)
        {
            await Task.CompletedTask;
            foreach (var row in rows)
            {
                token.ThrowIfCancellationRequested();
                var cells = new DocumentCell[columns.Length];
                foreach (var item in row)
                    cells[item.Column] = DocumentCell.Text(item.Text);
                yield return new([.. cells]);
            }
        }
    }
}
