using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;
using Em.Controls.Grid.Menus;
using Em.Controls.Grid.Query;

namespace Em.Ui.Wpf.Controls;

internal sealed class GridRowView : Canvas
{
    public static readonly DependencyProperty OwnerProperty = DependencyProperty.Register(
        nameof(Owner), typeof(MatrixGrid), typeof(GridRowView),
        new PropertyMetadata(null, (element, _) => ((GridRowView)element).Synchronize()));

    internal const double CellPadding = 12;
    internal const double CellFontSize = 14;
    private string? _contextColumn;
    private readonly Dictionary<string, ContentPresenter> _cellTemplates = new(StringComparer.Ordinal);
    private ContentPresenter? _details;
    private Control? _editor;
    private string? _editorColumn;

    public GridRowView()
    {
        Height = MatrixGrid.RowHeight;
        ClipToBounds = true;
        DataContextChanged += (_, _) => Synchronize();
        Loaded += (_, _) => Synchronize();
        Unloaded += (_, _) => Owner?.OnRowUnloaded(this);
    }

    public MatrixGrid? Owner
    {
        get => (MatrixGrid?)GetValue(OwnerProperty);
        set => SetValue(OwnerProperty, value);
    }

    public GridRow? Row => DataContext as GridRow;

    public void Synchronize()
    {
        if (Owner is not { } grid || Row is not { } row)
        {
            ClearPresenters();
            return;
        }
        Width = grid.SurfaceWidth;
        Height = row.Group is { IsFooter: true } ? 34 : row.Group is not null ? 40 : MatrixGrid.RowHeight;
        if (ContextMenu is null)
        {
            PreviewMouseRightButtonDown += (_, e) => _contextColumn = Owner?.ColumnAt(e.GetPosition(this).X)?.Key;
            grid.AttachGridMenu(this, () =>
            {
                var target = Row;
                return new MenuHit(target?.Group is { IsFooter: true } ? MenuRegion.GroupFooter :
                        target?.Group is not null ? MenuRegion.GroupRow : MenuRegion.RowCell,
                    target?.Group is { IsFooter: false } header ? header.ColumnKey : _contextColumn,
                    target?.Key.Value, target?.Group?.Level);
            });
        }
        AutomationProperties.SetName(this, string.Join(", ", grid.RenderColumns().Select(entry => grid.Display(row, entry.Column))));
        grid.OnRowRebound(this);
        UpdatePresenters(grid, row);
        if (_editor is { } editor && _editorColumn is { } key && grid.GetColumn(key) is { } column)
            PositionEditor(editor, column);
        InvalidateVisual();
    }

    public void PlaceEditor(Control editor, GridColumn column)
    {
        if (!Children.Contains(editor))
            Children.Add(editor);
        _editor = editor;
        _editorColumn = column.Key;
        Panel.SetZIndex(editor, 3);
        editor.HorizontalContentAlignment = MatrixGrid.HorizontalAlignmentFor(column);
        PositionEditor(editor, column);
        if (Owner is { } grid && Row is { } row)
            UpdatePresenters(grid, row);
    }

    private void PositionEditor(Control editor, GridColumn column)
    {
        if (Owner is not { } grid)
            return;
        double left = grid.VisualColumnLeft(column);
        double width = MatrixGrid.WidthFor(column);
        if (editor is Em.Ui.Wpf.Controls.ValueEditor { PreferredWidth: > 0 } preferred && preferred.PreferredWidth > width)
        {
            // Widen the editor over the neighbouring cells instead of clipping its content. Keep it inside the row.
            width = preferred.PreferredWidth;
            left = Math.Max(0, Math.Min(left, grid.SurfaceWidth - width));
        }
        SetLeft(editor, left);
        SetTop(editor, 0);
        editor.Width = width;
        editor.Height = MatrixGrid.RowHeight;
        editor.MinHeight = 0;
        ClipBehindFrozen(editor, grid, column, left, editor.Width);
    }

    public void RemoveEditor(Control editor)
    {
        Children.Remove(editor);
        _editor = null;
        _editorColumn = null;
        if (Owner is { } grid && Row is { } row)
            UpdatePresenters(grid, row);
    }

    private void UpdatePresenters(MatrixGrid grid, GridRow row)
    {
        if (row.Group is not null)
        {
            ClearPresenters();
            return;
        }

        var needed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (column, left, width) in grid.RenderColumns())
        {
            if (_editorColumn == column.Key || grid.PresentationFor(column)?.Template is not { } template)
                continue;
            needed.Add(column.Key);
            if (!_cellTemplates.TryGetValue(column.Key, out var presenter))
            {
                presenter = new ContentPresenter { IsHitTestVisible = false, Focusable = false };
                _cellTemplates.Add(column.Key, presenter);
                Children.Add(presenter);
            }
            presenter.ContentTemplate = template;
            presenter.Content = grid.CellInfo(row, column);
            presenter.Width = width;
            presenter.Height = MatrixGrid.RowHeight;
            SetLeft(presenter, left);
            Panel.SetZIndex(presenter, column.IsFrozen ? 2 : 1);
            ClipBehindFrozen(presenter, grid, column, left, width);
        }
        foreach (var key in _cellTemplates.Keys.Where(key => !needed.Contains(key)).ToArray())
        {
            Children.Remove(_cellTemplates[key]);
            _cellTemplates.Remove(key);
        }

        if (grid.RowDetailsTemplate is { } details && grid.IsRowDetailsOpen(row.Key))
        {
            if (_details is null)
            {
                _details = new ContentPresenter();
                SetTop(_details, MatrixGrid.RowHeight);
                Children.Add(_details);
            }
            _details.ContentTemplate = details;
            _details.Content = row;
            _details.Width = Math.Max(0, grid.ViewportWidth - 24);
            _details.Height = grid.RowDetailsHeight;
            SetLeft(_details, grid.ViewportLeft + 12);
            Height = MatrixGrid.RowHeight + grid.RowDetailsHeight;
        }
        else
            ClearDetails();
    }

    private void ClearPresenters()
    {
        foreach (var presenter in _cellTemplates.Values)
            Children.Remove(presenter);
        _cellTemplates.Clear();
        ClearDetails();
    }

    private void ClearDetails()
    {
        if (_details is null)
            return;
        Children.Remove(_details);
        _details = null;
    }

    private static void ClipBehindFrozen(FrameworkElement element, MatrixGrid grid, GridColumn column, double left, double width)
    {
        double covered = column.IsFrozen ? 0 : Math.Clamp(grid.ViewportLeft + grid.FrozenViewportWidth - left, 0, width);
        element.Clip = covered > 0
            ? new RectangleGeometry(new Rect(covered, 0, width - covered, MatrixGrid.RowHeight))
            : null;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (Owner is not { } grid || Row is not { } row)
            return;

        if (row.Group is { } group)
        {
            DrawGroup(drawing, grid, row, group);
            return;
        }

        bool selected = grid.State.SelectedRows.Contains(row.Key);
        double total = Math.Max(ActualWidth, grid.ViewportWidth);
        double rowHeight = MatrixGrid.RowHeight;
        var bounds = new Rect(0, 0, total, rowHeight);
        drawing.DrawRectangle(Brush(selected ? "SecondaryContainer" : "SurfaceContainerLowest"), null, bounds);
        if (IsMouseOver)
            drawing.DrawRectangle(Brush("StateHover"), null, bounds);
        if (selected)
            drawing.DrawRectangle(Brush("Primary"), null, new Rect(grid.ViewportLeft, 0, 4, rowHeight));


        foreach (var (column, left, width) in grid.RenderColumns())
            DrawCell(drawing, grid, row, column, left, width, selected);
        if (ActualHeight > rowHeight)
            drawing.DrawRectangle(Brush("SurfaceContainerLow"), null, new Rect(0, rowHeight, total, ActualHeight - rowHeight));
        drawing.DrawRectangle(Brush("OutlineVariant"), null, new Rect(0, rowHeight - 1, total, 1));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        if (Owner is not { } grid || Row is not { } row || (row.Group is null && point.Y >= MatrixGrid.RowHeight) ||
            grid.ColumnAt(point.X) is not { } column)
            return;
        grid.Focus();
        grid.SelectCell(new CellAddress(row.Key, column.Key),
            (Keyboard.Modifiers & ModifierKeys.Control) != 0, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        if (row.Group is { IsFooter: false } group)
            grid.ToggleGroup(group);
        else if (e.ClickCount == 2)
            grid.BeginEdit();
        e.Handled = true;
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    private void DrawCell(DrawingContext drawing, MatrixGrid grid, GridRow row, GridColumn column,
        double x, double width, bool selected)
    {
        double rowHeight = MatrixGrid.RowHeight;
        var cell = new Rect(x, 0, width, rowHeight);
        var presentation = grid.PresentationFor(column);
        if (column.IsFrozen)
        {
            drawing.DrawRectangle(Brush(selected ? "SecondaryContainer" : "SurfaceContainerLowest"), null, cell);
            if (IsMouseOver)
                drawing.DrawRectangle(Brush("StateHover"), null, cell);
            if (selected && x <= grid.ViewportLeft)
                drawing.DrawRectangle(Brush("Primary"), null, new Rect(x, 0, 4, rowHeight));
        }
        if (!selected && presentation?.Background?.Invoke(grid.CellInfo(row, column)) is { } background)
            drawing.DrawRectangle(background, null, cell);

        var address = new CellAddress(row.Key, column.Key);
        if (grid.State.FocusedCell == address)
            drawing.DrawRectangle(null, new Pen(Brush("Primary"), 2), new Rect(x + 1, 1, width - 2, rowHeight - 2));
        bool activeMatch = grid.IsActiveMatch(address);
        if (grid.EditingCell == address || presentation?.Template is not null)
        {
            if (activeMatch)
                drawing.DrawRectangle(null, new Pen(Brush("Primary"), 2), new Rect(x + 2, 2, width - 4, rowHeight - 4));
            return;
        }

        var value = grid.Display(row, column);
        if (string.IsNullOrEmpty(value))
            return;
        var foreground = Brush(selected ? "OnSecondaryContainer" : "OnSurface");
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(grid.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            CellFontSize, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, width - 2 * CellPadding),
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = MatrixGrid.TextAlignmentFor(column)
        };
        double y = (rowHeight - text.Height) / 2;
        var origin = new Point(x + CellPadding, y);
        var outline = activeMatch ? new Pen(Brush("Primary"), 2) : null;
        foreach (var (start, length) in grid.HighlightSpans(row.Key, column.Key, value))
        {
            if (text.BuildHighlightGeometry(origin, start, length) is not { } geometry)
                continue;
            drawing.DrawGeometry(Brush(activeMatch ? "PrimaryContainer" : "TertiaryContainer"), outline, geometry);
            text.SetForegroundBrush(Brush(activeMatch ? "OnPrimaryContainer" : "OnTertiaryContainer"), start, length);
        }
        drawing.DrawText(text, origin);
    }

    private void DrawGroup(DrawingContext drawing, MatrixGrid grid, GridRow row, GroupRowInfo group)
    {
        drawing.DrawRectangle(Brush("SurfaceContainerLow"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (grid.State.FocusedCell?.Row == row.Key)
            drawing.DrawRectangle(null, new Pen(Brush("Primary"), 2), new Rect(1, 1, Math.Max(1, ActualWidth - 2), ActualHeight - 2));
        drawing.DrawRectangle(Brush("OutlineVariant"), null, new Rect(0, ActualHeight - 1, ActualWidth, 1));
        if (group.IsFooter)
        {
            foreach (var (column, left, width) in grid.RenderColumns())
            {
                if (column.IsFrozen)
                    drawing.DrawRectangle(Brush("SurfaceContainerLow"), null, new Rect(left, 0, width, ActualHeight - 1));
                DrawLabel(grid.SummaryText(row.Summaries, column, SummaryScope.Group), left + CellPadding, width - 2 * CellPadding);
            }
        }
        else
        {
            var header = grid.GetColumn(group.ColumnKey)?.Header ?? group.ColumnKey;
            var value = group.Value.Kind == GridValueKind.Null ? "(Blank)" : group.Value.ToString();
            double left = grid.ViewportLeft + 8 + group.Level * 24;
            DrawLabel($"{(group.IsExpanded ? "▾" : "▸")}  {header}: {value}  ({group.Count:N0})", left,
                Math.Max(1, grid.ViewportWidth - 16 - group.Level * 24));
        }

        void DrawLabel(string label, double x, double width)
        {
            if (string.IsNullOrEmpty(label))
                return;
            var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(grid.FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal),
                group.IsFooter ? 12 : 14, Brush("OnSurface"), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = Math.Max(1, width), Trimming = TextTrimming.CharacterEllipsis };
            drawing.DrawText(text, new Point(x, (ActualHeight - text.Height) / 2));
        }
    }

    private Brush Brush(string role) => Owner?.GetBrush(role)
        ?? System.Windows.Media.Brushes.Transparent;
}
