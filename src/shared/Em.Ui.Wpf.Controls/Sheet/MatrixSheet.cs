using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Em.Editing;
using Em.Controls.Sheet.Commands;
using Em.Controls.Sheet.Cells;
using Em.Controls.Sheet.IO;
using Em.Controls.Sheet.Model;
using Em.Controls.Sheet.View;
using Em.Ui.Wpf.Controls;
using Microsoft.Win32;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixSheet : EditorControl
{
    private SheetSession? session;
    private SheetSurface? surface;
    private Canvas? cellLayer;
    private SheetCellEditor? cellEditor;
    private TextBox? nameBox, formulaBox;
    private StackPanel? tabs;
    private TextBlock? status;
    private DockPanel? body;
    private TextBox? search;
    private FrameworkElement? findReplacement;
    private FrameworkElement? replaceAction;
    private bool refreshing;
    private double zoom = 1;
    private CellAddress? editing;
    private string? editText;
    private SheetClipboard? copied;
    private readonly Dictionary<SheetTab, (int Row, int Column, double RowOffset, double ColumnOffset)> offsets = new();

    /// <summary>Gets the editing session of the displayed workbook; a new workbook is created on first use.</summary>
    public SheetSession Session => session ??= CreateSession(new());
    /// <summary>Gets the cell being edited in place, or null.</summary>
    public CellAddress? EditCell => editing;
    /// <summary>Gets the text of the in-cell editor, or null when no edit is open.</summary>
    public string? EditText => editText;
    /// <summary>Gets the first scrolled row shown below the frozen rows.</summary>
    public int FirstRow { get; private set; }
    /// <summary>Gets the first scrolled column shown right of the frozen columns.</summary>
    public int FirstColumn { get; private set; }
    /// <summary>Gets the zoom factor, where 1 is 100 percent.</summary>
    public double Zoom => zoom;

    /// <inheritdoc/>
    protected override IReadOnlyList<EditorCommand> Catalog => SheetCommands.Catalog;
    /// <inheritdoc/>
    protected override bool HandlesTextEditing => editing.HasValue;

    /// <summary>Shows a workbook in a new editing session; the previous undo history is discarded.</summary>
    /// <param name="workbook">The workbook.</param>
    public void LoadWorkbook(SheetWorkbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        if (session is not null)
            session.DocumentChanged -= SessionChanged;
        session = CreateSession(workbook);
        offsets.Clear();
        FirstRow = FirstColumn = 0;
        RowOffset = ColumnOffset = 0;
        usedExtent = null;
        pendingRows = pendingColumns = 0;
        EndEdit(false);
        Refresh();
    }

    /// <summary>Scrolls so that a row and a column are the first scrolled cells shown.</summary>
    /// <param name="row">The zero-based row.</param>
    /// <param name="column">The zero-based column.</param>
    public void ScrollTo(int row, int column)
    {
        FirstRow = Math.Clamp(row, 0, CellAddress.MaxRows - 1);
        FirstColumn = Math.Clamp(column, 0, CellAddress.MaxColumns - 1);
        RowOffset = ColumnOffset = 0;
        pendingRows = pendingColumns = 0;
        ScrollChangedVisual();
    }

    /// <summary>
    /// Starts editing a cell. Typed <paramref name="text"/> starts Enter mode, where an arrow commits and moves; without it
    /// the cell's own input is edited in Edit mode, where arrows move the caret, as F2 does in Excel.
    /// </summary>
    public bool BeginEdit(CellAddress address, string? text = null) => BeginEdit(address, text, null);

    /// <summary>Starts editing with the caret placed at <paramref name="click"/>, a point on the sheet surface.</summary>
    internal bool BeginEdit(CellAddress address, string? text, Point? click)
    {
        if (Mode != EditorMode.Full || !address.IsValid)
            return false;
        if (!Session.Selection.Contains(address))
            Session.Select(new(address, address));
        editing = address;
        editText = text ?? Session.ActiveSheet[address].Input;
        ShowCellEditor(text is not null);
        Refresh();
        if (cellEditor is null)
        {
            formulaBox?.Focus();
            if (formulaBox is { } input)
                input.CaretIndex = input.Text.Length;
        }
        else
            cellEditor.Focus(text is null ? click : null);
        return true;
    }

    /// <summary>Replaces the text of the open in-cell edit.</summary>
    /// <param name="text">The new text.</param>
    public void UpdateEditText(string text)
    {
        if (!editing.HasValue || Mode == EditorMode.Viewer)
            return;
        editText = text;
        cellEditor?.SetText(text);
        PositionCellEditor();
        Refresh();
    }

    /// <summary>The in-cell editor's text changed: the formula bar and the cell show it.</summary>
    internal void EditTextChanged(string text)
    {
        if (!editing.HasValue)
            return;
        editText = text;
        PositionCellEditor();
        Refresh();
    }

    /// <summary>Commits the in-cell edit and moves the active cell, as Enter, Tab and Enter-mode arrows do.</summary>
    internal void CommitCellEdit(int rows, int columns)
    {
        if (!EndEdit(true))
        {
            surface?.Focus();
            return;
        }
        var next = SheetNavigation.Step(Session.ActiveSheet, Session.ActiveCell, rows, columns);
        SelectAndReveal(next, next);
        surface?.Focus();
    }

    internal void CancelCellEdit()
    {
        EndEdit(false);
        surface?.Focus();
    }

    /// <summary>Whether a click on the sheet inserts a reference into the formula being edited.</summary>
    internal bool CanPoint => cellEditor?.CanPoint ?? false;

    /// <summary>The range the formula being edited points at, outlined on the sheet.</summary>
    internal CellRange? PointRange => cellEditor?.PointedRange;

    internal void PointAt(CellAddress cell, bool extend) => cellEditor?.PointAt(cell, extend);

    internal void PointDrag(CellAddress cell) => cellEditor?.PointDrag(cell);

    internal Point TranslateToInput(Point surfacePoint, UIElement input) =>
        surface?.TranslatePoint(surfacePoint, input) ?? surfacePoint;

    /// <summary>Closes the in-cell edit.</summary>
    /// <param name="commit">True to store the text, false to discard it.</param>
    /// <returns>True when the edit was closed; false when the text was rejected.</returns>
    public bool EndEdit(bool commit)
    {
        if (editing is not { } address)
            return false;
        string text = editText ?? "";
        editing = null;
        editText = null;
        if (cellEditor is { } closing)
        {
            cellEditor = null;
            closing.Close();
            cellLayer?.Children.Remove(closing);
        }
        bool applied = commit && Session.SetCell(address, text).Accepted;
        Refresh();
        return !commit || applied;
    }

    /// <inheritdoc/>
    protected override void ApplyMode(EditorMode mode)
    {
        if (mode == EditorMode.Viewer && editing.HasValue)
            EndEdit(true);
        if (session is not null)
            session.Mode = mode;
        if (formulaBox is not null)
            formulaBox.IsReadOnly = mode == EditorMode.Viewer;
        if (findReplacement is not null)
            findReplacement.Visibility = mode == EditorMode.Full ? Visibility.Visible : Visibility.Collapsed;
        if (replaceAction is not null)
            replaceAction.Visibility = mode == EditorMode.Full ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    /// <inheritdoc/>
    protected override (object Document, IReadOnlyList<string> Warnings) ReadFile(string path, Stream stream, CancellationToken cancellation)
    {
        var opened = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) ?
            SheetFiles.OpenCsv(stream, cancellation: cancellation) : SheetFiles.OpenXlsx(stream, cancellation);
        return (opened.Workbook, opened.Warnings);
    }

    /// <inheritdoc/>
    protected override void InstallDocument(object document) => LoadWorkbook((SheetWorkbook)document);

    /// <inheritdoc/>
    protected override UIElement BuildEditor()
    {
        body = new DockPanel();
        var formula = new DockPanel { Height = 40, Margin = new Thickness(8, 0, 8, 0) };
        Material.Paint(formula, Panel.BackgroundProperty, "SurfaceContainerLow");
        nameBox = Field(96);
        nameBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter && CellRange.TryParse(nameBox.Text, out var range))
            {
                SelectAndReveal(range);
                surface?.Focus();
            }
        };
        formula.Children.Add(nameBox);
        foreach (var (icon, name, action) in new (string, string, Action)[]
        {
            ("Close", "Cancel edit", () => EndEdit(false)), ("Check", "Commit edit", () => EndEdit(true)),
            ("Function", "Insert function", () => ExecuteCommand("function"))
        })
        {
            var button = Material.IconButton(icon, name, 28);
            button.Focusable = false;
            button.Click += (_, _) => { action(); surface?.Focus(); };
            formula.Children.Add(button);
        }

        formulaBox = Field(double.NaN);
        formulaBox.IsReadOnly = Mode == EditorMode.Viewer;
        formulaBox.TextChanged += (_, _) =>
        {
            if (refreshing || Mode == EditorMode.Viewer)
                return;
            editing ??= Session.ActiveCell;
            editText = formulaBox.Text;
            cellEditor?.SetText(editText);
            PositionCellEditor();
            surface?.InvalidateVisual();
        };
        formulaBox.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Enter or Key.Escape))
                return;
            EndEdit(args.Key == Key.Enter);
            surface?.Focus();
            args.Handled = true;
        };
        formula.Children.Add(formulaBox);
        DockPanel.SetDock(formula, Dock.Top);
        body.Children.Add(formula);

        status = new TextBlock { Height = 32, Padding = new Thickness(8, 8, 0, 0), FontSize = 12 };
        Material.Paint(status, TextBlock.BackgroundProperty, "SurfaceContainer");
        DockPanel.SetDock(status, Dock.Bottom);
        body.Children.Add(status);
        tabs = new StackPanel { Orientation = Orientation.Horizontal, Height = 40 };
        Material.Paint(tabs, Panel.BackgroundProperty, "SurfaceContainerLow");
        DockPanel.SetDock(tabs, Dock.Bottom);
        body.Children.Add(tabs);
        surface = new(this);
        AttachCellMenu(surface);
        cellLayer = new Canvas { ClipToBounds = true };
        var layers = new System.Windows.Controls.Grid();
        layers.ColumnDefinitions.Add(new ColumnDefinition());
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layers.RowDefinitions.Add(new RowDefinition());
        layers.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layers.Children.Add(surface);
        copyMarkLayer = new CopyMarkLayer(this, surface);
        layers.Children.Add(copyMarkLayer);
        layers.Children.Add(cellLayer);
        BuildScrollBars(layers);
        body.Children.Add(layers);
        Unloaded += (_, _) => { FinishAnimation(); copyMarkTimer?.Stop(); };
        Loaded += (_, _) => { if (copyMark is not null) copyMarkTimer?.Start(); };
        Refresh();
        return body;
    }

    private void ShowCellEditor(bool enterMode)
    {
        if (cellLayer is null || surface?.Viewport is null || editing is not { } address ||
            surface.Viewport.CellBounds(address) is null)
            return;
        if (cellEditor is { } previous)
        {
            previous.Close();
            cellLayer.Children.Remove(previous);
        }
        cellEditor = new SheetCellEditor(this, address, editText ?? "", enterMode);
        cellLayer.Children.Add(cellEditor);
        PositionCellEditor();
    }

    internal void PositionCellEditor()
    {
        if (cellEditor is null || editing is not { } address || surface?.Viewport is not { } viewport)
            return;
        // A cell scrolled out of view keeps its editor, and the keyboard, just off the visible layer.
        if (viewport.CellBounds(address) is not { } item)
        {
            Canvas.SetLeft(cellEditor, -100_000);
            Canvas.SetTop(cellEditor, -100_000);
            return;
        }
        var shown = viewport.Cells.FirstOrDefault(cell => cell.Address == address);
        var style = shown?.Style ?? new SheetStyle();
        cellEditor.ApplyStyle(style, zoom, style.FillColor is { } fill ? Brush(fill) : null,
            style.TextColor is { } color ? Brush(color) : null);
        Canvas.SetLeft(cellEditor, item.X);
        Canvas.SetTop(cellEditor, item.Y);
        // Excel widens the editor over the cells to its right as the text grows, up to the edge of the sheet.
        double room = Math.Max(item.Width, (cellLayer?.ActualWidth ?? item.X + item.Width) - item.X);
        cellEditor.Width = Math.Clamp(cellEditor.TextWidth(), item.Width, room);
        cellEditor.Height = item.Height;

        static Brush Brush(uint color)
        {
            var brush = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
            brush.Freeze();
            return brush;
        }
    }

    private static TextBox Field(double width)
    {
        var box = new TextBox { Height = 28, Width = width, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
        Material.Paint(box, TextBox.BackgroundProperty, "SurfaceContainerLowest");
        Material.Paint(box, TextBox.ForegroundProperty, "OnSurface");
        Material.Paint(box, TextBox.BorderBrushProperty, "OutlineVariant");
        return box;
    }

    private SheetSession CreateSession(SheetWorkbook workbook)
    {
        var current = new SheetSession(workbook) { Mode = Mode };
        current.DocumentChanged += SessionChanged;
        return current;
    }

    private void SessionChanged(object? sender, EventArgs args)
    {
        if (!keepCopyMark)
            ClearCopyMark();
        usedExtent = null;
        Refresh();
        RaiseDocumentChanged();
    }

    private void Refresh()
    {
        if (formulaBox is null || nameBox is null || status is null || tabs is null)
            return;
        refreshing = true;
        try
        {
            string formula = editText ?? Session.ActiveSheet[Session.ActiveCell].Input;
            if (formulaBox.Text != formula)
                formulaBox.Text = formula;
            nameBox.Text = Session.Selection.ToString();
            status.Text = StatusText();
            tabs.Children.Clear();
            foreach (var sheet in Session.Workbook.Sheets.Where(tab => !tab.IsHidden))
            {
                var button = Material.Button(sheet.Name, "TextButton");
                button.Focusable = false;
                button.Click += (_, _) =>
                {
                    EndEdit(true);
                    FinishAnimation();
                    offsets[Session.ActiveSheet] = (FirstRow, FirstColumn, RowOffset, ColumnOffset);
                    Session.ActivateSheet(sheet);
                    var offset = offsets.GetValueOrDefault(sheet);
                    ScrollTo(offset.Row, offset.Column);
                    (RowOffset, ColumnOffset) = (offset.RowOffset, offset.ColumnOffset);
                    Refresh();
                };
                if (sheet == Session.ActiveSheet)
                    Material.Paint(button, ForegroundProperty, "Primary");
                tabs.Children.Add(button);
            }
            if (Mode == EditorMode.Full)
            {
                var add = Material.IconButton("Plus", "Add sheet");
                add.Click += (_, _) => ExecuteCommand("add-sheet");
                tabs.Children.Add(add);
            }
        }
        finally
        {
            refreshing = false;
        }
        surface?.InvalidateVisual();
        RefreshToolbar();
    }

    internal void SelectAndReveal(CellRange selection) => SelectAndReveal(selection.Start, selection.End);

    /// <summary>Selects from an anchor (the active cell) to a moving end, and scrolls the moving end into view.</summary>
    internal void SelectAndReveal(CellAddress anchor, CellAddress end)
    {
        Session.Select(anchor, end);
        Reveal(end);
        Refresh();
    }

    /// <summary>Releases the session events and the editor resources.</summary>
    public override void Dispose()
    {
        StopAnimation();
        copyMarkTimer?.Stop();
        if (session is not null)
            session.DocumentChanged -= SessionChanged;
        base.Dispose();
    }
}
