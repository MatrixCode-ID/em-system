using System.Collections;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;
using Em.Editors;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;

namespace Em.Ui.Wpf.Controls;

[TemplatePart(Name = "PART_Host", Type = typeof(ContentPresenter))]
public sealed partial class MatrixGrid : Control
{
    internal const double RowHeight = 36;
    private const double HeaderHeight = 44;
    private const string ColumnDragFormat = "MatrixGrid.ColumnKey";

    /// <summary>Identifies the <see cref="ItemsSource"/> dependency property.</summary>
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(MatrixGrid), new PropertyMetadata(null, SourceChanged));

    /// <summary>Identifies the <see cref="Provider"/> dependency property.</summary>
    public static readonly DependencyProperty ProviderProperty = DependencyProperty.Register(
        nameof(Provider), typeof(IGridDataProvider), typeof(MatrixGrid), new PropertyMetadata(null, SourceChanged));

    /// <summary>Identifies the <see cref="Query"/> dependency property.</summary>
    public static readonly DependencyProperty QueryProperty = DependencyProperty.Register(
        nameof(Query), typeof(GridQuery), typeof(MatrixGrid), new FrameworkPropertyMetadata(GridQuery.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, QueryChanged), value => value is GridQuery);

    /// <summary>Identifies the <see cref="AutoGenerateColumns"/> dependency property.</summary>
    public static readonly DependencyProperty AutoGenerateColumnsProperty = DependencyProperty.Register(
        nameof(AutoGenerateColumns), typeof(bool), typeof(MatrixGrid), new PropertyMetadata(true, SourceChanged));

    private readonly ObjectRows _objects = new();
    private readonly HashSet<GridRowView> _realized = [];
    private readonly HashSet<INotifyPropertyChanged> _subscribedObjects = new(ReferenceEqualityComparer.Instance);
    private readonly DispatcherTimer _searchTimer;
    private GridColumn[] _generatedColumns = [];
    private IReadOnlyList<GridColumn> _displayColumns = Array.Empty<GridColumn>();
    private INotifyCollectionChanged? _subscribedCollection;
    private InMemoryGridDataProvider? _local;
    private CancellationTokenSource? _load;
    private ListBox? _list;
    private ScrollViewer? _bodyScroll;
    private ScrollViewer? _headerScroll;
    private Canvas? _header;
    private Border? _searchBar;
    private TextBox? _search;
    private TextBlock? _message;
    private GridRowView? _editingView;
    private IGridEditor? _editor;
    private GridEditSession? _edit;
    private CancellationTokenSource? _editCancellation;
    private CellAddress? _pendingNavigation;
    private RowKey? _selectionAnchor;
    private int _sourceQueued;
    private bool _updatingSearch;
    private bool _rebuildingHeader;

    static MatrixGrid() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(MatrixGrid), new FrameworkPropertyMetadata(typeof(MatrixGrid)));

    /// <summary>Creates an empty grid. Bind <see cref="ItemsSource"/> or <see cref="Provider"/> to show data.</summary>
    public MatrixGrid()
    {
        Columns.CollectionChanged += (_, _) =>
        {
            if (!_updatingColumns)
                RebuildColumns();
        };
        CellPresentations.CollectionChanged += (_, _) => RefreshPresentations();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        _searchTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(140), DispatcherPriority.Background,
            (_, _) => ApplySearch(), Dispatcher);
        _searchTimer.Stop();
    }

    /// <summary>Gets or sets an in-memory collection of objects to show. Public properties become columns when <see cref="AutoGenerateColumns"/> is set, and objects that raise property change notifications update their rows.</summary>
    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>Gets or sets a data provider that filters, sorts, groups and pages the data itself, for large or remote sources. It takes precedence over <see cref="ItemsSource"/>.</summary>
    public IGridDataProvider? Provider
    {
        get => (IGridDataProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    /// <summary>Gets or sets the sort, filter, search, grouping and summary query. Binds two-way by default.</summary>
    public GridQuery Query
    {
        get => (GridQuery)GetValue(QueryProperty);
        set => SetValue(QueryProperty, value);
    }

    /// <summary>Gets or sets whether columns are created from the public properties of <see cref="ItemsSource"/> objects when <see cref="Columns"/> is empty. The default is true.</summary>
    public bool AutoGenerateColumns
    {
        get => (bool)GetValue(AutoGenerateColumnsProperty);
        set => SetValue(AutoGenerateColumnsProperty, value);
    }

    /// <summary>Gets the columns in display order.</summary>
    public ObservableCollection<GridColumn> Columns { get; } = [];
    /// <summary>Gets the selection and focus state.</summary>
    public GridState State { get; private set; } = new();
    /// <summary>Gets the rows currently loaded in the view, including group rows.</summary>
    public IReadOnlyList<GridRow> VisibleRows { get; private set; } = Array.Empty<GridRow>();
    /// <summary>Gets the cell being edited, or null when no edit is open.</summary>
    public CellAddress? EditingCell => _edit?.Address;
    /// <summary>Gets the current status or error message shown by the grid, or null.</summary>
    public string? StatusMessage => _message?.Text;
    /// <summary>Occurs when the selection, focus or loaded rows change.</summary>
    public event EventHandler? StateChanged;

    internal IReadOnlyList<GridColumn> DisplayColumns => _displayColumns;
    internal double ContentWidth => _columnOffsets[^1];
    internal double ViewportWidth => _bodyScroll?.ViewportWidth ?? 0;
    internal double SurfaceWidth => Math.Max(ContentWidth, double.IsFinite(ViewportWidth) ? ViewportWidth : 0);

    /// <summary>Builds the grid surface when the template is applied.</summary>
    public override void OnApplyTemplate()
    {
        if (_bodyScroll is not null)
            _bodyScroll.ScrollChanged -= OnBodyScrolled;
        DetachEditor();
        _bodyScroll = null;
        _realized.Clear();
        _headers.Clear();
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Host") is ContentPresenter host)
        {
            host.Content = BuildSurface();
            RebuildColumns();
            _list!.ItemsSource = VisibleRows;
            if (IsSearchPanelVisible)
            {
                if (_finderCriteria is { } search)
                    SetFinderText(search.Text);
                else
                    SeedFinder();
                UpdateFinderResults();
            }
        }
    }

    /// <summary>Sorts by a column, cycling ascending, descending and, when appending, removed.</summary>
    /// <param name="columnKey">The column to sort by.</param>
    /// <param name="append">True to add the column to the existing sort; false to replace it.</param>
    public void SortBy(string columnKey, bool append = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        if (!_displayColumns.Any(column => column.Key == columnKey))
            return;

        var descriptors = Query.Sort;
        int existingIndex = -1;
        for (int index = 0; index < descriptors.Length; index++)
            if (descriptors[index].ColumnKey == columnKey)
                existingIndex = index;

        var next = append ? descriptors.ToBuilder() : ImmutableArray.CreateBuilder<SortDescriptor>();
        if (existingIndex < 0)
            next.Add(new SortDescriptor(columnKey));
        else if (!descriptors[existingIndex].Descending)
        {
            var descriptor = new SortDescriptor(columnKey, true);
            if (append)
                next[existingIndex] = descriptor;
            else
                next.Add(descriptor);
        }
        else if (append)
            next.RemoveAt(existingIndex);
        SetCurrentValue(QueryProperty, Query with { Sort = next.ToImmutable() });
    }

    /// <summary>Moves a column next to another column.</summary>
    /// <param name="columnKey">The column to move.</param>
    /// <param name="anchorKey">The column to move next to.</param>
    /// <param name="after">True to place it after the anchor; false to place it before.</param>
    /// <returns>True when the column moved.</returns>
    public bool MoveColumn(string columnKey, string anchorKey, bool after = false)
    {
        EnsureExplicitColumns();
        int from = IndexOfColumn(columnKey);
        int to = IndexOfColumn(anchorKey);
        if (from < 0 || to < 0 || from == to)
            return false;
        int insertion = to + (after ? 1 : 0);
        Columns.Move(from, insertion > from ? insertion - 1 : insertion);
        return true;
    }

    /// <summary>Focuses a cell and updates the row selection.</summary>
    /// <param name="address">The cell to focus.</param>
    /// <param name="toggle">True to toggle the row in the selection.</param>
    /// <param name="extendRange">True to select every row between the anchor row and this row.</param>
    public void SelectCell(CellAddress address, bool toggle = false, bool extendRange = false)
    {
        if (!VisibleRows.Any(row => row.Key == address.Row) || !_displayColumns.Any(column => column.Key == address.ColumnKey))
            return;
        if (EditingCell is { } editing && editing != address)
            CancelEdit();

        if (extendRange && _selectionAnchor is { } anchor)
        {
            int start = FindRow(anchor);
            int end = FindRow(address.Row);
            if (start >= 0 && end >= 0)
            {
                var range = VisibleRows.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1)
                    .Select(row => row.Key).ToImmutableHashSet();
                State = State with { SelectedRows = range, FocusedCell = address };
            }
            else
                State = State.Select(address, toggle);
        }
        else
        {
            _selectionAnchor = address.Row;
            State = State.Select(address, toggle);
        }
        PublishState();
    }

    /// <summary>Opens an in-place editor on the focused cell when its column and row can be edited.</summary>
    /// <returns>True when an editor opened.</returns>
    public bool BeginEdit()
    {
        if (_edit is not null || _list is null || State.FocusedCell is not { } address)
            return false;
        int rowIndex = FindRow(address.Row);
        var column = _displayColumns.FirstOrDefault(column => column.Key == address.ColumnKey);
        if (rowIndex < 0 || column is null || column.IsReadOnly)
            return false;

        var row = VisibleRows[rowIndex];
        if (row.Group is not null)
            return false;
        ScrollTo(address);
        _list.UpdateLayout();
        if (_realized.FirstOrDefault(view => view.Row?.Key == row.Key) is not { } view)
            return false;

        var value = row.Values.GetValueOrDefault(column.Key);
        var settings = column.Editor ?? DefaultEditor(column.Kind);
        if (settings.IsReadOnly)
            return false;
        var valueType = _objects.GetValueType(address);
        _editor = EditorFactory.Create(settings, valueType);
        _edit = new GridEditSession(address, value);
        _editCancellation = new CancellationTokenSource();
        _editingView = view;
        _editor.EditValue = ObjectRows.Unwrap(value);
        _editor.Host = new GridEditorHost(this);
        view.PlaceEditor(_editor.Element, column);
        view.InvalidateVisual();
        view.UpdateLayout();
        _editor.FocusEditor();
        UpdateFinderStatus(pendingFilter: !ReferenceEquals(_rowsSearch, FinderFilter ?? Query.Search));
        return true;
    }

    /// <summary>Commits the open editor.</summary>
    /// <returns>True when the value was accepted.</returns>
    public bool CommitEdit() => _editor?.Commit() ?? false;

    /// <summary>Closes the open editor without storing its value.</summary>
    public void CancelEdit()
    {
        if (_edit is null || _edit.Status == EditStatus.Committing)
            return;
        _edit.Cancel();
        DetachEditor();
    }

    /// <summary>Handles grid keyboard navigation, editing and shortcut keys.</summary>
    /// <param name="e">The key event data.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (HoldsInput(e))
            e.Handled = true;
        base.OnPreviewKeyDown(e);
        if (e.Handled)
            return;

        if (HandleFinderKey(e))
        {
            e.Handled = true;
            return;
        }

        if (!ReferenceEquals(e.OriginalSource, this))
            return;

        if (OpenFocusedMenu(e.Key == Key.System ? e.SystemKey : e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            CopyGridRow(FocusedMenuHit(), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
            return;
        }
        if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control && CanExport())
        {
            ExecuteGridMenu("print", FocusedMenuHit());
            e.Handled = true;
            return;
        }

        if (State.FocusedCell is { } selected && VisibleRows.FirstOrDefault(row => row.Key == selected.Row)?.Group is { IsFooter: false } group
            && (e.Key is Key.Enter or Key.Space || e.Key == Key.Left && group.IsExpanded || e.Key == Key.Right && !group.IsExpanded))
        {
            ToggleGroup(group);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.F2)
            e.Handled = BeginEdit();
        else if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Tab or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            e.Handled = Navigate(e.Key, Keyboard.Modifiers);
    }

    /// <summary>Reacts to theme and layout property changes.</summary>
    /// <param name="e">The property change data.</param>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ForegroundProperty || e.Property == BackgroundProperty)
            foreach (var row in _realized)
                row.InvalidateVisual();
    }

    internal static double WidthFor(GridColumn column) => double.IsFinite(column.Width)
        ? Math.Clamp(column.Width, 44, 2000) : 120;

    internal double ColumnLeft(string key)
    {
        double left = _columnOffsets[0];
        foreach (var column in _displayColumns)
        {
            if (column.Key == key)
                return left;
            left += WidthFor(column);
        }
        return left;
    }

    internal GridColumn? ColumnAt(double x)
    {
        double local = x - HorizontalOffset;
        int index = ColumnIndexAt(FrozenWidth > 0 && local < FrozenWidth ? local : x);
        return index >= 0 ? _displayColumns[index] : null;
    }

    internal string Display(GridRow row, GridColumn column) =>
        column.Format(row.Values.GetValueOrDefault(column.Key), CultureInfo.CurrentCulture);

    internal void OnRowRebound(GridRowView view)
    {
        _realized.Add(view);
        if (ReferenceEquals(_editingView, view) && view.Row?.Key != EditingCell?.Row)
            DetachEditor();
    }

    internal void OnRowUnloaded(GridRowView view)
    {
        _realized.Remove(view);
        if (ReferenceEquals(_editingView, view))
            DetachEditor();
    }

    private static EditorSettings DefaultEditor(GridValueKind kind) => kind switch
    {
        GridValueKind.Number => new NumericSettings { Culture = CultureInfo.CurrentCulture, AllowNull = true },
        GridValueKind.Boolean => new CheckSettings { AllowNull = true },
        GridValueKind.Date => new DateSettings { Culture = CultureInfo.CurrentCulture, AllowNull = true },
        _ => new TextSettings { AllowNull = true, SelectAllOnFocus = true }
    };

    private void PublishState()
    {
        foreach (var row in _realized.ToArray())
            row.Synchronize();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private int FindRow(RowKey key)
    {
        for (int index = 0; index < VisibleRows.Count; index++)
            if (VisibleRows[index].Key == key)
                return index;
        return -1;
    }

    private int IndexOfColumn(string key)
    {
        for (int index = 0; index < Columns.Count; index++)
            if (Columns[index].Key == key)
                return index;
        return -1;
    }

    private void EnsureExplicitColumns()
    {
        if (Columns.Count == 0)
            foreach (var column in _generatedColumns)
                Columns.Add(column);
    }

    private bool Navigate(Key key, ModifierKeys modifiers)
    {
        if (VisibleRows.Count == 0 || _displayColumns.Count == 0)
            return false;
        if (_edit?.Status == EditStatus.Committing)
        {
            _pendingNavigation = NavigationTarget(key, modifiers);
            return true;
        }

        if (NavigateAcrossWindow(key, State.FocusedCell is { } current ? FindRow(current.Row) : 0))
            return true;

        var next = NavigationTarget(key, modifiers);
        if (next is null)
            return false;
        SelectCell(next.Value, extendRange: key != Key.Tab && (modifiers & ModifierKeys.Shift) != 0);
        ScrollTo(next.Value);
        Focus();
        return true;
    }

    private CellAddress? NavigationTarget(Key key, ModifierKeys modifiers)
    {
        int row = 0, column = 0;
        if (State.FocusedCell is { } current)
        {
            row = Math.Max(0, FindRow(current.Row));
            for (int index = 0; index < _displayColumns.Count; index++)
                if (_displayColumns[index].Key == current.ColumnKey)
                    column = index;
        }

        switch (key)
        {
            case Key.Up: row--; break;
            case Key.Down: row++; break;
            case Key.Left: column--; break;
            case Key.Right: column++; break;
            case Key.PageUp: row -= Math.Max(1, (int)((_bodyScroll?.ViewportHeight ?? 0) / RowHeight)); break;
            case Key.PageDown: row += Math.Max(1, (int)((_bodyScroll?.ViewportHeight ?? 0) / RowHeight)); break;
            case Key.Home: column = 0; break;
            case Key.End: column = _displayColumns.Count - 1; break;
            case Key.Tab:
                column += (modifiers & ModifierKeys.Shift) != 0 ? -1 : 1;
                if (column < 0) { row--; column = _displayColumns.Count - 1; }
                else if (column >= _displayColumns.Count) { row++; column = 0; }
                if (row < 0 || row >= VisibleRows.Count)
                    return null;
                break;
            default: return null;
        }

        return new CellAddress(VisibleRows[Math.Clamp(row, 0, VisibleRows.Count - 1)].Key,
            _displayColumns[Math.Clamp(column, 0, _displayColumns.Count - 1)].Key);
    }

    private void ScrollTo(CellAddress address)
    {
        int index = FindRow(address.Row);
        if (index >= 0)
            _list?.ScrollIntoView(VisibleRows[index]);
        if (_bodyScroll is not { } scroll)
            return;
        double left = ColumnLeft(address.ColumnKey);
        if (_displayColumns.FirstOrDefault(column => column.Key == address.ColumnKey)?.IsFrozen == true)
            return;
        if (left < scroll.HorizontalOffset + FrozenWidth)
            scroll.ScrollToHorizontalOffset(Math.Max(0, left - FrozenWidth));
        else if (GetColumn(address.ColumnKey) is { } column && left + WidthFor(column) > scroll.HorizontalOffset + scroll.ViewportWidth)
            scroll.ScrollToHorizontalOffset(left + WidthFor(column) - scroll.ViewportWidth);
    }

    private async void ApplyEdit(object? value)
    {
        if (_edit is not { Status: EditStatus.Active } edit || _editCancellation is not { } cancellation)
            return;
        try
        {
            bool updated;
            if (Provider is null && _objects.TrySet(edit.Address, value))
                updated = true;
            else if ((Provider ?? _local) is { } provider)
                updated = await edit.CommitAsync(provider, ObjectRows.ToValue(value), cancellation.Token) == CellUpdateResult.Updated;
            else
                updated = false;

            if (!ReferenceEquals(_edit, edit))
                return;
            if (!updated)
            {
                ShowMessage("The row changed before the edit could be saved.");
                return;
            }

            var next = _pendingNavigation;
            DetachEditor();
            if (Provider is null && ItemsSource is not null && _objects.GetValueType(edit.Address) is not null)
                RebuildSource();
            else
                await RefreshAsync();
            if (next is { } destination)
            {
                SelectCell(destination);
                ScrollTo(destination);
            }
            if (_searchBar?.IsKeyboardFocusWithin != true)
                Focus();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_edit, edit))
                ShowMessage(ex.GetBaseException().Message);
        }
    }

    private void DetachEditor()
    {
        bool wasEditing = _edit is not null;
        _editCancellation?.Cancel();
        _editCancellation?.Dispose();
        _editCancellation = null;
        if (_editor is { } editor)
        {
            editor.Host = null;
            _editingView?.RemoveEditor(editor.Element);
            _editingView?.InvalidateVisual();
        }
        _editor = null;
        _edit = null;
        _editingView = null;
        _pendingNavigation = null;
        if (wasEditing)
            ResumeFinderAfterEdit();
    }

    private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var grid = (MatrixGrid)sender;
        grid._requestedOffset = 0;
        grid._matchNavigation?.Cancel();
        if (args.Property == ItemsSourceProperty || args.Property == ProviderProperty)
        {
            grid.DetachEditor();
            grid._detailsRows.Clear();
            grid._recentFilters.Clear();
        }
        if (grid.IsLoaded)
            grid.RebuildSource();
    }

    private static void QueryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var grid = (MatrixGrid)sender;
        grid._requestedOffset = 0;
        grid._matchNavigation?.Cancel();
        var query = (GridQuery)args.NewValue;
        if (query.Filter is { } filter && query.FilterEnabled && !Equals(((GridQuery)args.OldValue).Filter, filter))
        {
            grid._recentFilters.Remove(filter);
            grid._recentFilters.Insert(0, filter);
            if (grid._recentFilters.Count > 5)
                grid._recentFilters.RemoveAt(5);
        }
        grid.State = grid.State with { Query = query };
        grid.RebuildColumns(refreshFinderFilter: false);
        if (grid.IsSearchPanelVisible && grid._search is not null && !Equals(((GridQuery)args.OldValue).Search, query.Search))
            grid.SeedFinder(refreshFilter: false);
        grid.PublishState();
        if (grid.IsLoaded)
            _ = grid.RefreshAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        ApplyTemplate();
        RebuildSource();
    }

    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _searchTimer.Stop();
        _matchNavigation?.Cancel();
        _matchNavigation = null;
        _finderScan?.Cancel();
        _finderScan = null;
        _load?.Cancel();
        _load = null;
        _waitingLoad = null;
        UpdateWaiter();
        DetachEditor();
        _finderRefreshAfterEdit = false;
        Unsubscribe();
        _objects.Clear();
        _realized.Clear();
    }

    private void Unsubscribe()
    {
        if (_subscribedCollection is not null)
            _subscribedCollection.CollectionChanged -= OnCollectionChanged;
        _subscribedCollection = null;
        foreach (var model in _subscribedObjects)
            model.PropertyChanged -= OnObjectChanged;
        _subscribedObjects.Clear();
    }

    private void RebuildSource()
    {
        if (!IsLoaded)
            return;
        Unsubscribe();
        try
        {
            if (Provider is null)
            {
                var snapshot = _objects.Snapshot(ItemsSource);
                _local = new InMemoryGridDataProvider(snapshot.Rows);
                _generatedColumns = snapshot.Columns;
                _subscribedCollection = ItemsSource as INotifyCollectionChanged;
                if (_subscribedCollection is not null)
                    _subscribedCollection.CollectionChanged += OnCollectionChanged;
                foreach (var model in _objects.ObservedObjects)
                    if (_subscribedObjects.Add(model))
                        model.PropertyChanged += OnObjectChanged;
            }
            else
            {
                _objects.Clear();
                _local = null;
                _generatedColumns = [];
            }
            RebuildColumns(refreshFinderFilter: false);
            _ = RefreshAsync();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.GetBaseException().Message);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => ScheduleRebuild();
    private void OnObjectChanged(object? sender, PropertyChangedEventArgs args) => ScheduleRebuild();

    private void ScheduleRebuild()
    {
        if (Interlocked.Exchange(ref _sourceQueued, 1) != 0)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
        {
            Interlocked.Exchange(ref _sourceQueued, 0);
            RebuildSource();
        }));
    }

    /// <summary>Reloads the visible rows from the data source.</summary>
    /// <returns>A task that completes when the rows are loaded.</returns>
    public Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    private async Task RefreshAsync(CancellationToken cancellationToken, bool preserveEdit = false, int? requestedOffset = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsLoaded || _list is null || (Provider ?? _local) is not { } provider)
            return;
        _load?.Cancel();
        var request = _load = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : new CancellationTokenSource();
        var query = EffectiveQuery;
        bool applyingSearch = query.Search is { FilterRows: true } ||
            _rowsSearch is { FilterRows: true } && !ReferenceEquals(_rowsSearch, query.Search);
        bool runLocalSearchInBackground = applyingSearch && provider is InMemoryGridDataProvider
            && (query.Search?.DisplayFormatter is null || CanSearchOnWorker(_finderColumns))
            && (!query.FilterEnabled || CanMatchOnWorker(query.Filter));
        _waitingLoad = applyingSearch ? request : null;
        if (applyingSearch)
            _finderSearchFailed = false;
        try
        {
            UpdateWaiter();
            int offset = Provider is null ? 0 : requestedOffset ?? _requestedOffset;
            int count = Provider is null ? int.MaxValue : VirtualWindowSize;
            var range = new RowRange(offset, count);
            var token = request.Token;
            var page = runLocalSearchInBackground
                ? await Task.Run(async () => await provider.FetchAsync(query, range, token), token)
                : await provider.FetchAsync(query, range, token);
            if (!ReferenceEquals(_load, request) || request.IsCancellationRequested)
                return;
            if (preserveEdit && EditingCell is not null)
            {
                _finderRefreshAfterEdit = true;
                return;
            }
            _rowsSearch = query.Search;
            _requestedOffset = offset;
            TotalRowCount = page.TotalCount;
            ResultRowCount = page.DataCount;
            _totals = page.Totals;
            VisibleRowOffset = offset;
            SetRows(page.Rows);
            if (!ReferenceEquals(_load, request) || request.IsCancellationRequested)
                return;
            UpdateVirtualScrollbar();
            RenderTotalFooter();
            UpdateFilterPanel();
            await UpdateFinderResultsAsync(cancellationToken: token);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_load, request) && !request.IsCancellationRequested)
            {
                if (applyingSearch)
                {
                    _finderSearchFailed = true;
                    UpdateFinderStatus(pendingFilter: true);
                }
                ShowMessage(ex.GetBaseException().Message);
            }
        }
        finally
        {
            if (ReferenceEquals(_load, request))
                _load = null;
            if (ReferenceEquals(_waitingLoad, request))
            {
                _waitingLoad = null;
                UpdateWaiter();
            }
            request.Dispose();
        }
    }

    private static bool CanMatchOnWorker(FilterExpression? expression) => expression switch
    {
        null or FilterCondition or DatePeriodFilter => true,
        AllFilters all => all.Terms.All(CanMatchOnWorker),
        AnyFilter any => any.Terms.All(CanMatchOnWorker),
        NegatedFilter negated => CanMatchOnWorker(negated.Term),
        ColumnFilter column => CanMatchOnWorker(column.Term),
        _ => false
    };

    private void SetRows(IReadOnlyList<GridRow> rows)
    {
        VisibleRows = rows;
        _list!.ItemsSource = rows;
        ShowMessage(rows.Count == 0 ? "No rows" : null);
        PublishState();
    }

    private void ShowMessage(string? text)
    {
        if (_message is not { } message)
            return;
        message.Text = text ?? string.Empty;
        message.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private sealed class GridEditorHost(MatrixGrid grid) : IInPlaceEditorHost
    {
        public void Commit(object? value) => grid.ApplyEdit(value);
        public void Cancel() => grid.CancelEdit();
        public bool Navigate(Key key, ModifierKeys modifiers) => grid.Navigate(key, modifiers);
    }
}
