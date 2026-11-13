using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Em.Grid;
using Em.Grid.Data;
using Em.Editors;
using Em.Grid.Query;

namespace Em.Ui.Wpf.Documents.Grid;

public sealed partial class MatrixGrid
{
    /// <summary>Identifies the <see cref="IsSearchPanelVisible"/> dependency property.</summary>
    public static readonly DependencyProperty IsSearchPanelVisibleProperty = DependencyProperty.Register(
        nameof(IsSearchPanelVisible), typeof(bool), typeof(MatrixGrid),
        new PropertyMetadata(false, (element, _) => ((MatrixGrid)element).UpdateSearchVisibility()));

    /// <summary>Gets or sets whether the search panel is shown.</summary>
    public bool IsSearchPanelVisible
    {
        get => (bool)GetValue(IsSearchPanelVisibleProperty);
        set => SetValue(IsSearchPanelVisibleProperty, value);
    }

    /// <summary>Gets or sets the key gesture that opens the finder. The default is Ctrl+F; null disables it.</summary>
    public KeyGesture? SearchGesture { get; set; } = new(Key.F, ModifierKeys.Control);

    private CancellationTokenSource? _matchNavigation;
    private CancellationTokenSource? _finderScan;
    private GridColumn[] _finderColumns = [];
    private SearchCriteria? _finderCriteria;
    private SearchCriteria? _rowsSearch;
    private SearchCriteria? _highlightCriteria;
    private SearchMatcher? _highlightMatcher;
    private bool _finderFilterRows;
    private bool _finderCaseSensitive;
    private bool _finderWholeWord;
    private bool _finderRefreshAfterEdit;
    private bool _finderScrollToFirst;
    private bool _finderSearchFailed;
    private List<CellAddress> _finderMatches = [];
    private HashSet<RowKey> _finderMatchingRows = [];
    private int _finderIndex = -1;

    // The host query remains available while the temporary finder filter is applied to data operations.
    private SearchCriteria? FinderFilter => _finderCriteria is { FilterRows: true } ? _finderCriteria : null;
    private SearchCriteria? ApiSearch => _finderCriteria ?? Query.Search;
    private GridQuery EffectiveQuery => FinderFilter is { } search ? Query with { Search = search } : Query;
    private CellAddress? ActiveMatch => _finderIndex >= 0 ? _finderMatches[_finderIndex] : null;

    /// <summary>Moves focus to the next or previous loaded row that matches the search.</summary>
    /// <param name="previous">True to search backwards.</param>
    /// <returns>True when a match was found.</returns>
    public bool FindNext(bool previous = false)
    {
        if (EditingCell is not null || ApiSearch is not { Text.Length: > 0 } search || VisibleRows.Count == 0 || _displayColumns.Count == 0)
            return false;

        var matches = search.Compile();
        int start = State.FocusedCell is { } current ? FindRow(current.Row) : -1;
        if (start < 0)
            start = previous ? 0 : -1;
        for (int step = 1; step <= VisibleRows.Count; step++)
        {
            int index = (start + (previous ? -step : step) + VisibleRows.Count) % VisibleRows.Count;
            var row = VisibleRows[index];
            if (row.Group is not null || !matches(row.Values))
                continue;
            return SelectMatch(row, search);
        }
        return false;
    }

    /// <summary>Moves focus to the next or previous matching row across the whole data source, loading rows as needed.</summary>
    /// <param name="previous">True to search backwards.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>True when a match was found.</returns>
    public async Task<bool> FindNextAsync(bool previous = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Provider is not { } source || TotalRowCount is not > 0)
            return FindNext(previous);
        var query = EffectiveQuery;
        if (EditingCell is not null || ApiSearch is not { Text.Length: > 0 } criteria || _displayColumns.Count == 0)
            return false;

        _matchNavigation?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _matchNavigation = request;
        var token = request.Token;
        try
        {
            UpdateWaiter();
            int total = TotalRowCount.Value;
            int direction = previous ? -1 : 1;
            int focused = State.FocusedCell is { } cell ? FindRow(cell.Row) : -1;
            int start = focused >= 0 ? VisibleRowOffset + focused + direction
                : previous ? VisibleRowOffset + VisibleRows.Count - 1 : VisibleRowOffset;
            int cursor = start < 0 ? total - 1 : start >= total ? 0 : start;
            var matches = criteria.Compile();

            for (int remaining = total; remaining > 0;)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(Math.Min(remaining, 256), previous ? cursor + 1 : total - cursor);
                int offset = previous ? cursor - count + 1 : cursor;
                var page = await source.FetchAsync(query, new RowRange(offset, count), token);
                var rows = page.Rows;
                for (int step = 0; step < rows.Length; step++)
                {
                    int index = previous ? rows.Length - 1 - step : step;
                    var row = rows[index];
                    if (row.Group is not null || !matches(row.Values))
                        continue;
                    token.ThrowIfCancellationRequested();
                    if (FindRow(row.Key) < 0)
                        await GoToRowAsync(offset + index, token);
                    token.ThrowIfCancellationRequested();
                    return EditingCell is null && FindRow(row.Key) >= 0 && SelectMatch(row, criteria);
                }
                remaining -= count;
                cursor += direction * count;
                if (cursor < 0 || cursor == total)
                    cursor = previous ? total - 1 : 0;
            }
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && token.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (ReferenceEquals(_matchNavigation, request))
            {
                _matchNavigation = null;
                UpdateWaiter();
            }
        }
    }

    internal static ImmutableArray<GridValueLabel> DisplayLabelsFor(GridColumn column)
    {
        var values = ImmutableArray.CreateBuilder<GridValueLabel>();
        if (column.Editor is Em.Editors.ChoiceSettings choices)
            foreach (var option in choices.Options)
                values.Add(new GridValueLabel(ObjectRows.ToValue(option.Value), option.Label));
        if (column.Editor is null && column.Kind == GridValueKind.Boolean)
        {
            values.Add(new(GridValue.From(true), "✓"));
            values.Add(new(GridValue.From(false), ""));
        }
        if (column.Editor is { NullText.Length: > 0 } settings)
            values.Add(new(default, settings.NullText));
        return values.ToImmutable();
    }

    private bool SelectMatch(GridRow row, SearchCriteria criteria)
    {
        var matcher = new SearchMatcher(criteria);
        var column = _displayColumns.FirstOrDefault(c => matcher.Spans(c.Key, Display(row, c)).Any()) ?? _displayColumns[0];
        var target = new CellAddress(row.Key, column.Key);
        SelectCell(target);
        ScrollTo(target);
        return true;
    }

    /// <summary>Shows the search panel and focuses its text box.</summary>
    public void ShowSearch()
    {
        if (!IsSearchPanelVisible)
            SetCurrentValue(IsSearchPanelVisibleProperty, true);
        _search?.Focus();
        _search?.SelectAll();
    }

    private void UpdateSearchVisibility()
    {
        if (_searchBar is not { } bar)
            return;
        bar.Visibility = IsSearchPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        if (IsSearchPanelVisible)
            SeedFinder();
        else
            ClearFinderSession();
    }

    private void SeedFinder(bool refreshFilter = true)
        => SeedFinderFrom(Query.Search, refreshFilter);

    private void SeedFinderFrom(SearchCriteria? search, bool refreshFilter = true)
    {
        _finderFilterRows = search?.FilterRows == true;
        _finderCaseSensitive = search?.CaseSensitive == true;
        _finderWholeWord = search?.MatchWholeWord == true;
        SetFinderText(search?.Text ?? string.Empty);
        ApplySearch(refreshFilter);
    }

    private void CloseFinder()
    {
        SetCurrentValue(IsSearchPanelVisibleProperty, false);
        if (_editor is { } editor)
            editor.FocusEditor();
        else
            Focus();
    }

    private void ClearFinderSession()
    {
        if (_searchOptionsMenu is { IsOpen: true } options)
            options.IsOpen = false;
        if (_gridSettingsMenu is { IsOpen: true } settings)
            settings.IsOpen = false;
        SetFinderText(string.Empty);
        ApplySearch();
    }

    private void SetFinderText(string text)
    {
        if (_search is null)
            return;
        _updatingSearch = true;
        _search.Text = text;
        _updatingSearch = false;
    }

    private bool HandleFinderKey(KeyEventArgs e)
    {
        if (SearchGesture?.Matches(this, e) == true)
        {
            ShowSearch();
            return true;
        }
        if (!IsSearchPanelVisible)
        {
            if (e.Key != Key.F3 || Query.Search is null)
                return false;
            _ = NavigateApiSearchAsync(Keyboard.Modifiers == ModifierKeys.Shift);
            return true;
        }

        bool onGrid = ReferenceEquals(e.OriginalSource, this);
        if (e.Key == Key.Escape && _editor?.Element.IsKeyboardFocusWithin != true)
        {
            CloseFinder();
            return true;
        }

        bool previous = Keyboard.Modifiers == ModifierKeys.Shift;
        if ((previous || Keyboard.Modifiers == ModifierKeys.None)
            && (e.Key == Key.F3 || e.Key == Key.Enter && (_search?.IsKeyboardFocusWithin == true || onGrid)))
        {
            NavigateFinder(previous);
            return true;
        }
        return false;
    }

    private async Task NavigateApiSearchAsync(bool previous)
    {
        try { await FindNextAsync(previous); }
        catch (Exception error) { ShowMessage(error.GetBaseException().Message); }
    }

    private void NavigateFinder(bool previous)
    {
        if (IsWaiting)
            return;
        if (_searchTimer.IsEnabled)
        {
            ApplySearch();
            return;
        }
        if (_finderSearchFailed || _finderMatches.Count < 2 || EditingCell is not null)
            return;
        _finderIndex = (_finderIndex + (previous ? -1 : 1) + _finderMatches.Count) % _finderMatches.Count;
        PaintFinderResults();
        ScrollTo(_finderMatches[_finderIndex]);
    }

    private void ApplySearch() => ApplySearch(refreshFilter: true);

    private void ScheduleFinderSearch()
    {
        _matchNavigation?.Cancel();
        if (FinderFilter is { } filter && !ReferenceEquals(_rowsSearch, filter))
            _load?.Cancel();
        _searchTimer.Stop();
        // Filtering rows queries the whole source, so wait for a typing pause before starting it.
        _searchTimer.Interval = TimeSpan.FromMilliseconds(_finderFilterRows ? 400 : 140);
        _searchTimer.Start();
        UpdateFinderStatus(pendingFilter: false);
    }

    private void ApplySearch(bool refreshFilter)
    {
        _searchTimer.Stop();
        _matchNavigation?.Cancel();
        _finderSearchFailed = false;
        var text = IsSearchPanelVisible ? _search?.Text.Trim() : null;
        var oldFilter = FinderFilter;
        _finderCriteria = string.IsNullOrEmpty(text) ? null : CreateFinderCriteria(text);
        _finderScrollToFirst = _finderCriteria is not null;
        UpdateFinderResults(resetActive: true);
        if (!ReferenceEquals(oldFilter, FinderFilter) && refreshFilter)
            RequestFinderRefresh(resetOffset: true);
    }

    private SearchCriteria CreateFinderCriteria(string text)
    {
        var columns = _finderColumns.ToDictionary(column => column.Key, StringComparer.Ordinal);
        var culture = CultureInfo.CurrentCulture;
        return new SearchCriteria(text, [.. columns.Keys], _finderFilterRows)
        {
            CaseSensitive = _finderCaseSensitive,
            MatchWholeWord = _finderWholeWord,
            RequireSearchColumns = true,
            CultureName = culture.Name,
            DisplayFormatter = (key, value) => columns[key].Format(value, culture),
            DisplayFormats = _finderColumns.Select(column => (column.Key,
                    Format: column.DisplayFormat ?? (column.Editor is null && column.Kind == GridValueKind.Date ? "dd MMM yyyy" : null)))
                .Where(entry => entry.Format is not null)
                .ToImmutableDictionary(entry => entry.Key, entry => entry.Format!, StringComparer.Ordinal),
            DisplayLabels = _finderColumns.Select(column => (column.Key, Labels: DisplayLabelsFor(column)))
                .Where(entry => !entry.Labels.IsEmpty)
                .ToImmutableDictionary(entry => entry.Key, entry => entry.Labels, StringComparer.Ordinal),
            DisplayCultures = _finderColumns.Where(column => column.Editor is not null)
                .ToImmutableDictionary(column => column.Key, column => column.Editor!.Culture.Name, StringComparer.Ordinal)
        };
    }

    private void UpdateFinderColumns(bool refreshFilter)
    {
        var columns = _displayColumns.Where(column => column.IsSearchable).ToArray();
        bool scopeChanged = !_finderColumns.Select(SearchColumn).SequenceEqual(columns.Select(SearchColumn));
        _finderColumns = columns;
        if (scopeChanged && IsSearchPanelVisible)
            ApplySearch(refreshFilter);

        static (string Key, GridValueKind Kind, Em.Editors.EditorSettings? Editor) SearchColumn(GridColumn column) =>
            (column.Key, column.Kind, column.Editor);
    }

    private void RequestFinderRefresh(bool resetOffset = false)
    {
        if (resetOffset)
            _requestedOffset = 0;
        if (EditingCell is not null)
        {
            _finderRefreshAfterEdit = true;
            return;
        }
        _ = RefreshAsync(CancellationToken.None, preserveEdit: true);
    }

    private void ResumeFinderAfterEdit()
    {
        if (!_finderRefreshAfterEdit && !IsSearchPanelVisible)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (EditingCell is not null || !IsLoaded)
                return;
            if (_finderRefreshAfterEdit)
            {
                _finderRefreshAfterEdit = false;
                RequestFinderRefresh();
            }
            else
                UpdateFinderResults();
        }));
    }

    private void UpdateFinderResults(bool resetActive = false) => _ = UpdateFinderResultsAsync(resetActive);

    private async Task UpdateFinderResultsAsync(bool resetActive = false, CancellationToken cancellationToken = default)
    {
        _finderScan?.Cancel();
        _finderScan = null;
        var previous = resetActive ? null : ActiveMatch;
        _finderIndex = -1;
        _finderMatches = [];
        _finderMatchingRows = [];
        bool pendingFilter = !ReferenceEquals(_rowsSearch, FinderFilter ?? Query.Search);
        if (!IsSearchPanelVisible || pendingFilter || _finderCriteria is not { } search)
        {
            UpdateWaiter();
            PaintFinderResults(pendingFilter);
            return;
        }

        var rows = VisibleRows;
        var columns = _finderColumns;
        var culture = CultureInfo.CurrentCulture;
        var scan = rows.Count * (long)columns.Length > 4096
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        _finderScan = scan;
        try
        {
            UpdateWaiter();
            if (scan is not null)
                PaintFinderResults();

            var matcher = new SearchMatcher(search);
            var matches = new List<CellAddress>();
            var matchingRows = new HashSet<RowKey>();
            if (!matcher.Terms.IsEmpty && columns.Length != 0)
            {
                if (scan is not null && CanSearchOnWorker(columns))
                    await Task.Run(() => ScanAsync(yieldToUi: false), scan.Token);
                else
                    await ScanAsync(yieldToUi: scan is not null);
            }
            if (scan?.IsCancellationRequested == true)
                return;
            _finderSearchFailed = false;
            _finderMatches = matches;
            _finderMatchingRows = matchingRows;
            if (_finderMatches.Count > 0)
            {
                _finderIndex = previous is { } address ? Math.Max(0, _finderMatches.IndexOf(address)) : 0;
                if (_finderScrollToFirst && EditingCell is null)
                {
                    _finderScrollToFirst = false;
                    ScrollTo(_finderMatches[_finderIndex]);
                }
            }

            async Task ScanAsync(bool yieldToUi)
            {
                var display = new string[columns.Length];
                var hits = new bool[columns.Length];
                var slots = Array.ConvertAll(columns.ToArray(), column => new GridColumnSlot(column.Key));
                long slice = Stopwatch.GetTimestamp();
                foreach (var row in rows)
                {
                    if (yieldToUi && Stopwatch.GetElapsedTime(slice).TotalMilliseconds >= 8)
                    {
                        await Dispatcher.Yield(DispatcherPriority.Background);
                        slice = Stopwatch.GetTimestamp();
                    }
                    scan?.Token.ThrowIfCancellationRequested();
                    if (row.Group is not null)
                        continue;
                    for (int index = 0; index < columns.Length; index++)
                        display[index] = columns[index].Format(slots[index].Get(row.Values), culture);
                    Array.Clear(hits);
                    if (!CollectMatches(matcher, columns, display, hits))
                        continue;
                    matchingRows.Add(row.Key);
                    for (int index = 0; index < hits.Length; index++)
                        if (hits[index])
                            matches.Add(new CellAddress(row.Key, columns[index].Key));
                }
            }
        }
        catch (OperationCanceledException) when (scan?.IsCancellationRequested == true) { }
        catch (Exception error)
        {
            if (scan?.IsCancellationRequested != true)
            {
                _finderSearchFailed = true;
                ShowMessage(error.GetBaseException().Message);
            }
        }
        finally
        {
            if (scan is null || ReferenceEquals(_finderScan, scan))
            {
                _finderScan = null;
                UpdateWaiter();
                PaintFinderResults();
            }
            scan?.Dispose();
        }
    }

    private static bool CanSearchOnWorker(GridColumn[] columns) => columns.All(column => column.Editor switch
    {
        null or TextSettings or NumericSettings or CheckSettings or DateSettings => true,
        ComboSettings choices => CanCompareOptionsOnWorker(choices),
        LookupSettings choices => CanCompareOptionsOnWorker(choices),
        _ => false
    });

    private static bool CanCompareOptionsOnWorker(ChoiceSettings choices) =>
        choices.Options.All(option => option.Value is null or string or decimal or bool or DateOnly
            or DateTime or byte or sbyte or short or ushort or int or uint or long or ulong or float or double);

    private static bool CollectMatches(SearchMatcher matcher, GridColumn[] columns, string[] display, bool[] hits)
    {
        foreach (var term in matcher.Terms)
        {
            bool found = false;
            for (int index = 0; index < columns.Length; index++)
            {
                if (term.ColumnKey is not null && !StringComparer.OrdinalIgnoreCase.Equals(term.ColumnKey, columns[index].Key)
                    || !matcher.Contains(display[index], term.Text))
                    continue;
                hits[index] = true;
                found = true;
            }
            if (!found)
                return false;
        }
        return true;
    }

    private void PaintFinderResults(bool pendingFilter = false)
    {
        UpdateFinderStatus(pendingFilter);
        foreach (var row in _realized)
            row.InvalidateVisual();
    }

    internal bool IsActiveMatch(CellAddress address) => ActiveMatch == address;

    internal IEnumerable<(int Start, int Length)> HighlightSpans(RowKey row, string key, string value)
    {
        var search = IsSearchPanelVisible ? _finderCriteria : Query.Search;
        if (search is not { Text.Length: > 0 } || IsSearchPanelVisible && !_finderMatchingRows.Contains(row))
            return [];
        if (!ReferenceEquals(search, _highlightCriteria))
        {
            _highlightCriteria = search;
            _highlightMatcher = new SearchMatcher(search);
        }
        return _highlightMatcher!.Spans(key, value);
    }
}
