using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;

namespace Em.Ui.Wpf.Controls;

/// <summary>Event data for <see cref="MatrixGrid.BeginningEdit"/>.</summary>
/// <param name="row">The row of the cell.</param>
/// <param name="column">The column of the cell.</param>
/// <param name="item">The row object: the source object for <see cref="MatrixGrid.ItemsSource"/>, or the row for a provider.</param>
public sealed class GridBeginningEditEventArgs(GridRow row, GridColumn column, object? item) : EventArgs
{
    /// <summary>Gets the row of the cell.</summary>
    public GridRow Row { get; } = row;
    /// <summary>Gets the column of the cell.</summary>
    public GridColumn Column { get; } = column;
    /// <summary>Gets the row object.</summary>
    public object? Item { get; } = item;
    /// <summary>Gets or sets whether the editor stays closed.</summary>
    public bool Cancel { get; set; }
}

/// <summary>How many rows the user can select in a <see cref="MatrixGrid"/>.</summary>
public enum GridSelectionMode
{
    /// <summary>One row at a time. Ctrl and Shift clicks select only the clicked row.</summary>
    Single,

    /// <summary>Several rows, with Ctrl to toggle a row and Shift to extend a range.</summary>
    Multiple
}

public sealed partial class MatrixGrid
{
    /// <summary>Identifies the <see cref="SelectionMode"/> dependency property.</summary>
    public static readonly DependencyProperty SelectionModeProperty = DependencyProperty.Register(
        nameof(SelectionMode), typeof(GridSelectionMode), typeof(MatrixGrid),
        new PropertyMetadata(GridSelectionMode.Multiple, (element, _) => ((MatrixGrid)element).OnSelectionModeChanged()));

    /// <summary>Identifies the <see cref="SelectedItem"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(MatrixGrid),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (element, args) => ((MatrixGrid)element).OnSelectedItemChanged(args.NewValue)));

    /// <summary>Identifies the <see cref="SelectedItems"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedItemsProperty = DependencyProperty.Register(
        nameof(SelectedItems), typeof(IList), typeof(MatrixGrid),
        new PropertyMetadata(null, (element, args) => ((MatrixGrid)element).OnSelectedItemsChanged(
            args.OldValue as IList, args.NewValue as IList)));

    /// <summary>Identifies the <see cref="RowDoubleClickCommand"/> dependency property.</summary>
    public static readonly DependencyProperty RowDoubleClickCommandProperty = DependencyProperty.Register(
        nameof(RowDoubleClickCommand), typeof(ICommand), typeof(MatrixGrid));

    /// <summary>
    /// Occurs before an in-place editor opens on a cell that the column allows to edit. Set
    /// <see cref="GridBeginningEditEventArgs.Cancel"/> to keep the cell read-only for that row.
    /// </summary>
    public event EventHandler<GridBeginningEditEventArgs>? BeginningEdit;

    private bool _syncingSelection;
    private object? _pendingSelectedItem;

    /// <summary>Gets or sets whether one or several rows can be selected. The default is <see cref="GridSelectionMode.Multiple"/>.</summary>
    public GridSelectionMode SelectionMode
    {
        get => (GridSelectionMode)GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the focused selected row. For <see cref="ItemsSource"/> the value is the source object; for
    /// <see cref="Provider"/> it is the <see cref="GridRow"/>. Binds two-way by default. Setting an object that is not
    /// loaded yet keeps it and selects it as soon as its row is loaded; setting null clears the selection.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets a list that the grid keeps equal to the selected rows, in display order. For <see cref="ItemsSource"/>
    /// it holds source objects; for <see cref="Provider"/> it holds <see cref="GridRow"/> values. When the list raises
    /// collection change notifications, changes made by the host select the matching rows. Fixed-size and read-only
    /// lists are read but never written.
    /// </summary>
    public IList? SelectedItems
    {
        get => (IList?)GetValue(SelectedItemsProperty);
        set => SetValue(SelectedItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets a command run when a data row is double-clicked and the clicked cell does not open an in-place editor.
    /// The parameter is the row object, as returned by <see cref="GetRowItem(RowKey)"/>.
    /// </summary>
    public ICommand? RowDoubleClickCommand
    {
        get => (ICommand?)GetValue(RowDoubleClickCommandProperty);
        set => SetValue(RowDoubleClickCommandProperty, value);
    }

    internal void ActivateRow(GridRow row)
    {
        if (row.Group is not null || RowDoubleClickCommand is not { } command)
            return;
        var item = GetRowItem(row.Key);
        if (command.CanExecute(item))
            command.Execute(item);
    }

    internal void SelectForMenu(GridRow row, string? columnKey)
    {
        if (row.Group is not null || State.SelectedRows.Contains(row.Key))
            return;
        if ((columnKey ?? FirstColumnKey()) is { } column)
            SelectCell(new CellAddress(row.Key, column));
    }

    /// <summary>Returns the object shown by a row: the source object for <see cref="ItemsSource"/>, or the loaded <see cref="GridRow"/> for <see cref="Provider"/>.</summary>
    /// <param name="key">The row key.</param>
    /// <returns>The row object, or null when the row is not loaded or is a group row.</returns>
    public object? GetRowItem(RowKey key)
    {
        if (Provider is null)
            return _objects.ItemFor(key) ?? DataRow(key);
        return DataRow(key);
    }

    /// <summary>Finds the key of the row that shows an object.</summary>
    /// <param name="item">A source object, or a <see cref="GridRow"/>.</param>
    /// <returns>The row key, or null when the object is not shown by the grid.</returns>
    public RowKey? GetRowKey(object? item) => item switch
    {
        null => null,
        GridRow row => row.Key,
        _ when Provider is null => _objects.KeyFor(item),
        _ => null
    };

    private GridRow? DataRow(RowKey key)
    {
        foreach (var row in VisibleRows)
            if (row.Key == key)
                return row.Group is null ? row : null;
        return null;
    }

    private void OnSelectionModeChanged()
    {
        if (SelectionMode != GridSelectionMode.Single || State.SelectedRows.Count <= 1)
            return;
        var keep = State.FocusedCell?.Row is { } focused && State.SelectedRows.Contains(focused)
            ? focused : OrderedSelection().FirstOrDefault();
        State = State with { SelectedRows = keep.Value is null ? [] : [keep] };
        PublishState();
    }

    private void OnSelectedItemChanged(object? item)
    {
        if (_syncingSelection)
            return;
        _pendingSelectedItem = null;
        if (item is null)
        {
            if (State.SelectedRows.Count == 0 && State.FocusedCell is null)
                return;
            State = State with { SelectedRows = [], FocusedCell = null };
            PublishState();
            return;
        }

        if (GetRowKey(item) is { } key && FindRow(key) >= 0)
            SelectRow(key, scroll: true);
        else
            _pendingSelectedItem = item;
    }

    private void OnSelectedItemsChanged(IList? previous, IList? list)
    {
        if (previous is INotifyCollectionChanged oldNotify)
            oldNotify.CollectionChanged -= OnSelectedItemsCollectionChanged;
        if (list is INotifyCollectionChanged notify)
            notify.CollectionChanged += OnSelectedItemsCollectionChanged;
        if (list is null)
            return;
        if (list.Count > 0)
            ApplySelectedItems(list);
        else
            WriteSelectedItems();
    }

    private void OnSelectedItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (!_syncingSelection && sender is IList list)
            ApplySelectedItems(list);
    }

    private void ApplySelectedItems(IList list)
    {
        var keys = new List<RowKey>();
        foreach (var item in list)
            if (GetRowKey(item) is { } key && FindRow(key) >= 0 && !keys.Contains(key))
                keys.Add(key);
        if (SelectionMode == GridSelectionMode.Single && keys.Count > 1)
            keys.RemoveRange(1, keys.Count - 1);
        var selected = keys.ToImmutableHashSet();
        if (selected.SetEquals(State.SelectedRows))
            return;
        var focus = State.FocusedCell is { } cell && selected.Contains(cell.Row) ? cell
            : keys.Count > 0 && FirstColumnKey() is { } column ? new CellAddress(keys[0], column) : (CellAddress?)null;
        State = State with { SelectedRows = selected, FocusedCell = focus };
        PublishState();
    }

    private void SelectRow(RowKey key, bool scroll)
    {
        var column = State.FocusedCell is { } cell && _displayColumns.Any(item => item.Key == cell.ColumnKey)
            ? cell.ColumnKey : FirstColumnKey();
        if (column is null)
        {
            State = State with { SelectedRows = [key] };
            PublishState();
            return;
        }
        var address = new CellAddress(key, column);
        SelectCell(address);
        if (scroll)
            ScrollTo(address);
    }

    private string? FirstColumnKey() => _displayColumns.Count > 0 ? _displayColumns[0].Key : null;

    private IEnumerable<RowKey> OrderedSelection()
    {
        foreach (var row in VisibleRows)
            if (row.Group is null && State.SelectedRows.Contains(row.Key))
                yield return row.Key;
    }

    /// <summary>Selects a requested item once its row has been loaded.</summary>
    private void ApplyPendingSelection()
    {
        if (_pendingSelectedItem is not { } pending || GetRowKey(pending) is not { } key || FindRow(key) < 0)
            return;
        _pendingSelectedItem = null;
        var column = FirstColumnKey();
        State = column is null ? State with { SelectedRows = [key] }
            : State with { SelectedRows = [key], FocusedCell = new CellAddress(key, column) };
        _selectionAnchor = key;
    }

    /// <summary>Copies the selection state into <see cref="SelectedItem"/> and <see cref="SelectedItems"/>.</summary>
    private void SyncSelectionProperties()
    {
        if (_syncingSelection)
            return;
        _syncingSelection = true;
        try
        {
            var ordered = OrderedSelection().ToArray();
            var primaryKey = State.FocusedCell?.Row is { } focused && State.SelectedRows.Contains(focused)
                ? focused : ordered.Length > 0 ? ordered[0] : (RowKey?)null;
            object? primary = primaryKey is { } key ? GetRowItem(key) : null;
            // Keep a requested item that is still waiting for its row, and keep the current item while its row is only
            // filtered out of view, so a reload does not clear the host selection.
            if (primary is not null || (_pendingSelectedItem is null && !SelectionHiddenByView()))
            {
                if (!Equals(SelectedItem, primary))
                    SetCurrentValue(SelectedItemProperty, primary);
            }
            WriteSelectedItems(ordered);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private bool SelectionHiddenByView() =>
        State.SelectedRows.Count > 0 && !State.SelectedRows.Any(key => FindRow(key) >= 0);

    private void WriteSelectedItems() => WriteSelectedItems(OrderedSelection().ToArray());

    private void WriteSelectedItems(RowKey[] ordered)
    {
        if (SelectedItems is not { IsReadOnly: false, IsFixedSize: false } list)
            return;
        var items = ordered.Select(GetRowItem).Where(item => item is not null).ToArray();
        if (list.Count == items.Length && items.Select((item, index) => Equals(list[index], item)).All(same => same))
            return;
        bool wasSyncing = _syncingSelection;
        _syncingSelection = true;
        try
        {
            list.Clear();
            foreach (var item in items)
                list.Add(item);
        }
        finally
        {
            _syncingSelection = wasSyncing;
        }
    }
}
