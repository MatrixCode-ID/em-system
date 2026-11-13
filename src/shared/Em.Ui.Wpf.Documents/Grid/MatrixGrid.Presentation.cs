using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Em.Grid;
using Em.Grid.Data;

namespace Em.Ui.Wpf.Documents.Grid;

public sealed partial class MatrixGrid
{
    /// <summary>Identifies the <see cref="RowDetailsTemplate"/> dependency property.</summary>
    public static readonly DependencyProperty RowDetailsTemplateProperty = DependencyProperty.Register(
        nameof(RowDetailsTemplate), typeof(DataTemplate), typeof(MatrixGrid),
        new PropertyMetadata(null, (element, _) => ((MatrixGrid)element).RefreshPresentations()));

    private readonly HashSet<RowKey> _detailsRows = [];
    private Canvas? _bands;

    /// <summary>Gets or sets the template of the details panel shown under an expanded row; its data context is the row.</summary>
    public DataTemplate? RowDetailsTemplate
    {
        get => (DataTemplate?)GetValue(RowDetailsTemplateProperty);
        set => SetValue(RowDetailsTemplateProperty, value);
    }

    /// <summary>Gets the per-column cell templates and backgrounds.</summary>
    public ObservableCollection<GridCellPresentation> CellPresentations { get; } = [];
    /// <summary>Identifies the <see cref="RowDetailsHeight"/> dependency property.</summary>
    public static readonly DependencyProperty RowDetailsHeightProperty = DependencyProperty.Register(
        nameof(RowDetailsHeight), typeof(double), typeof(MatrixGrid),
        new PropertyMetadata(82d, (element, _) => ((MatrixGrid)element).RefreshPresentations()),
        value => value is double height && double.IsFinite(height) && height > 0);

    /// <summary>Gets or sets the height of the row details panel.</summary>
    public double RowDetailsHeight
    {
        get => (double)GetValue(RowDetailsHeightProperty);
        set => SetValue(RowDetailsHeightProperty, value);
    }

    /// <summary>Checks whether the details panel of a row is open.</summary>
    /// <param name="row">The row key.</param>
    /// <returns>True when the details are shown.</returns>
    public bool IsRowDetailsOpen(RowKey row) => _detailsRows.Contains(row);

    /// <summary>Opens or closes the details panel of a row.</summary>
    /// <param name="key">The row key.</param>
    /// <returns>True when the details are now open.</returns>
    public bool ToggleRowDetails(RowKey key)
    {
        if (RowDetailsTemplate is null || !VisibleRows.Any(row => row.Key == key && row.Group is null))
            return false;
        if (!_detailsRows.Add(key))
            _detailsRows.Remove(key);
        RefreshPresentations();
        return true;
    }

    /// <summary>Pins a column at the leading edge or releases it.</summary>
    /// <param name="key">The column key.</param>
    /// <param name="frozen">True to freeze the column.</param>
    /// <returns>True when the column exists.</returns>
    public bool SetColumnFrozen(string key, bool frozen)
    {
        EnsureExplicitColumns();
        int index = IndexOfColumn(key);
        if (index < 0)
            return false;
        Columns[index] = Columns[index] with { IsFrozen = frozen };
        return true;
    }

    /// <summary>Redraws the realized cells after <see cref="CellPresentations"/> change.</summary>
    public void RefreshPresentations()
    {
        foreach (var row in _realized.ToArray())
            row.Synchronize();
    }

    internal GridCellPresentation? PresentationFor(GridColumn column) =>
        CellPresentations.LastOrDefault(candidate => candidate.ColumnKey == column.Key);

    internal GridCellInfo CellInfo(GridRow row, GridColumn column) => new(row, column,
        row.Values.GetValueOrDefault(column.Key), Display(row, column), State.SelectedRows.Contains(row.Key));

    private int FrozenColumnCount => _displayColumns.TakeWhile(column => column.IsFrozen).Count();
    private double FrozenWidth => FrozenColumnCount > 0 ? _columnOffsets[FrozenColumnCount] : 0;

    internal double FrozenViewportWidth => FrozenWidth;
    internal double VisualColumnLeft(GridColumn column) => ColumnLeft(column.Key) + (column.IsFrozen ? HorizontalOffset : 0);

    private FrameworkElement HeaderStack()
    {
        _bands = new Canvas { Height = 26, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left };
        _bands.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLow");
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(_bands);
        stack.Children.Add(_header!);
        return stack;
    }

    private void RenderBands()
    {
        if (_bands is null)
            return;
        _bands.Children.Clear();
        _bands.Width = SurfaceWidth;
        _bands.Visibility = _displayColumns.Any(column => !string.IsNullOrWhiteSpace(column.Band))
            ? Visibility.Visible : Visibility.Collapsed;
        if (_bands.Visibility != Visibility.Visible)
            return;

        foreach (var entry in BandSegments(RenderColumns().OrderBy(item => item.Left)))
        {
            var label = new TextBlock
            {
                Text = entry.Band, VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0)
            };
            label.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurfaceVariant");
            var band = new Border { Child = label, Width = entry.Width, Height = 26, BorderThickness = new Thickness(0, 0, 1, 1) };
            band.SetResourceReference(BorderBrushProperty, "MatrixGrid.Brush.OutlineVariant");
            band.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLow");
            Canvas.SetLeft(band, entry.Left);
            Panel.SetZIndex(band, entry.Frozen ? 1 : 0);
            _bands.Children.Add(band);
        }
    }

    private static IEnumerable<(string Band, double Left, double Width, bool Frozen)> BandSegments(
        IEnumerable<(GridColumn Column, double Left, double Width)> columns)
    {
        string? name = null;
        double start = 0, end = 0;
        bool fixedBand = false;
        foreach (var (column, left, width) in columns)
        {
            if (name is not null && (name != column.Band || fixedBand != column.IsFrozen || Math.Abs(left - end) > 0.01))
            {
                yield return (name, start, end - start, fixedBand);
                name = null;
            }
            if (string.IsNullOrWhiteSpace(column.Band))
                continue;
            if (name is null)
            {
                start = left;
                name = column.Band;
                fixedBand = column.IsFrozen;
            }
            end = left + width;
        }
        if (name is not null)
            yield return (name, start, end - start, fixedBand);
    }
}
