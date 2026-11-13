using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Em.Controls.Grid;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixGrid
{
    private ScrollBar? _virtualScroll;
    private bool _updatingVirtualScroll;
    private int _requestedOffset;
    private double[] _columnOffsets = [0];
    private readonly Dictionary<string, FrameworkElement> _headers = new(StringComparer.Ordinal);

    /// <summary>Gets the total number of display rows reported by the data provider, or null when unknown.</summary>
    public int? TotalRowCount { get; private set; }
    /// <summary>Gets the zero-based offset of the first loaded row.</summary>
    public int VisibleRowOffset { get; private set; }
    /// <summary>Gets the number of row elements currently realized on screen.</summary>
    public int RealizedRowCount => _realized.Count;
    /// <summary>Gets the number of column header elements currently realized.</summary>
    public int RealizedColumnHeaderCount => _headers.Count;

    internal double ViewportLeft => HorizontalOffset;
    private double HorizontalOffset => _bodyScroll?.HorizontalOffset ?? 0;
    private int VirtualWindowSize => _list is { ActualHeight: > 0 } list ? Math.Clamp((int)(list.ActualHeight / RowHeight), 1, 256) : 16;

    /// <summary>Scrolls so that a display row is loaded and visible.</summary>
    /// <param name="offset">The zero-based display row offset.</param>
    /// <returns>A task that completes when the rows are loaded.</returns>
    public Task GoToRowAsync(int offset) => GoToRowAsync(offset, CancellationToken.None);

    private async Task GoToRowAsync(int offset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (Provider is null)
        {
            if (offset < VisibleRows.Count)
                _list?.ScrollIntoView(VisibleRows[offset]);
            return;
        }
        int max = TotalRowCount is int total ? Math.Max(0, total - VirtualWindowSize) : int.MaxValue;
        int requested = Math.Min(offset, max);
        if (!cancellationToken.CanBeCanceled)
            _requestedOffset = requested;
        await RefreshAsync(cancellationToken, preserveEdit: cancellationToken.CanBeCanceled, requestedOffset: requested);
    }

    /// <summary>Scrolls horizontally so that a column is visible. Frozen columns are always visible.</summary>
    /// <param name="key">The column key.</param>
    public void ScrollColumnIntoView(string key)
    {
        if (_bodyScroll is not { } scroll || !_displayColumns.Any(column => column.Key == key))
            return;
        if (_displayColumns.First(column => column.Key == key).IsFrozen)
            return;
        scroll.ScrollToHorizontalOffset(Math.Max(0, ColumnLeft(key) - FrozenWidth));
    }

    private void BuildVirtualScrollbar()
    {
        _virtualScroll = new ScrollBar
        {
            Orientation = Orientation.Vertical,
            Visibility = Visibility.Collapsed,
            Minimum = 0,
            SmallChange = 1,
            LargeChange = 16
        };
        System.Windows.Automation.AutomationProperties.SetName(_virtualScroll, "Scroll grid rows");
        _virtualScroll.ValueChanged += (_, e) =>
        {
            if (!_updatingVirtualScroll && Provider is not null)
                _ = GoToRowAsync((int)Math.Round(e.NewValue));
        };
    }

    private void UpdateVirtualScrollbar()
    {
        if (_virtualScroll is null || _list is null)
            return;

        bool virtualized = Provider is not null;
        ScrollViewer.SetVerticalScrollBarVisibility(_list, virtualized ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto);
        _updatingVirtualScroll = true;
        try
        {
            _virtualScroll.Visibility = virtualized ? Visibility.Visible : Visibility.Collapsed;
            _virtualScroll.ViewportSize = VirtualWindowSize;
            _virtualScroll.LargeChange = Math.Max(1, VirtualWindowSize - 1);
            int logicalCount = TotalRowCount ?? Math.Max(0, VisibleRowOffset + VisibleRows.Count + 1);
            _virtualScroll.Maximum = Math.Max(0, logicalCount - VirtualWindowSize);
            _virtualScroll.Value = VisibleRowOffset;
        }
        finally { _updatingVirtualScroll = false; }
    }

    private void RecalculateColumnOffsets()
    {
        var offsets = new double[_displayColumns.Count + 1];
        offsets[0] = Query.Groups.Length * 24;
        for (int index = 0; index < _displayColumns.Count; index++)
            offsets[index + 1] = offsets[index] + WidthFor(_displayColumns[index]);
        _columnOffsets = offsets;
    }

    private int ColumnIndexAt(double x)
    {
        if (_displayColumns.Count == 0 || x < _columnOffsets[0] || x >= _columnOffsets[^1])
            return -1;
        int position = Array.BinarySearch(_columnOffsets, x);
        return position >= 0 ? position : ~position - 1;
    }

    internal IEnumerable<(GridColumn Column, double Left, double Width)> RenderColumns()
    {
        double start = HorizontalOffset;
        double viewport = ViewportWidth > 0 ? ViewportWidth : Math.Max(1200, ActualWidth);
        int frozen = FrozenColumnCount;
        double dataLeft = start + FrozenWidth;
        int index = dataLeft < _columnOffsets[0] && _displayColumns.Count > 0 ? 0 : ColumnIndexAt(dataLeft);
        if (index >= 0)
            for (index = Math.Max(frozen, index); index < _displayColumns.Count && _columnOffsets[index] < start + viewport + 24; index++)
                yield return (_displayColumns[index], _columnOffsets[index], _columnOffsets[index + 1] - _columnOffsets[index]);
        for (int pinned = 0; pinned < frozen && _columnOffsets[pinned] < viewport; pinned++)
            yield return (_displayColumns[pinned], start + _columnOffsets[pinned], _columnOffsets[pinned + 1] - _columnOffsets[pinned]);
    }

    private void PopulateVisibleHeaders(bool replace = false)
    {
        if (_header is null)
            return;
        _header.Width = SurfaceWidth;
        var visible = RenderColumns().ToArray();
        var wanted = visible.Select(entry => entry.Column.Key).ToHashSet(StringComparer.Ordinal);

        foreach (var key in _headers.Keys.ToArray())
        {
            if (!replace && wanted.Contains(key))
                continue;
            _header.Children.Remove(_headers[key]);
            _headers.Remove(key);
        }
        foreach (var (column, left, _) in visible)
        {
            if (!_headers.TryGetValue(column.Key, out var header))
            {
                header = CreateHeader(column);
                _headers.Add(column.Key, header);
                _header.Children.Add(header);
            }
            Canvas.SetLeft(header, left);
            Panel.SetZIndex(header, column.IsFrozen ? 1 : 0);
        }
        RenderBands();
    }

    private void InvalidateViewport()
    {
        PopulateVisibleHeaders();
        foreach (var row in _realized.ToArray())
            row.Synchronize();
    }

    private bool NavigateAcrossWindow(Key key, int visibleIndex)
    {
        if (Provider is null)
            return false;
        int target = key switch
        {
            Key.Up when visibleIndex <= 0 => VisibleRowOffset - 1,
            Key.Down when visibleIndex >= VisibleRows.Count - 2 => VisibleRowOffset + VisibleRows.Count - 1,
            Key.PageUp => VisibleRowOffset - Math.Max(1, VirtualWindowSize - 1),
            Key.PageDown => VisibleRowOffset + Math.Max(1, VirtualWindowSize - 1),
            _ => -1
        };
        if (target < 0 || target == VisibleRowOffset || (TotalRowCount is int count && target >= count))
            return false;
        string column = State.FocusedCell?.ColumnKey ?? _displayColumns[0].Key;
        _ = NavigateToRemoteRow(target, column);
        return true;
    }

    private async Task NavigateToRemoteRow(int target, string column)
    {
        await GoToRowAsync(target);
        int index = target - VisibleRowOffset;
        if (index >= 0 && index < VisibleRows.Count)
            SelectCell(new CellAddress(VisibleRows[index].Key, column));
    }
}
