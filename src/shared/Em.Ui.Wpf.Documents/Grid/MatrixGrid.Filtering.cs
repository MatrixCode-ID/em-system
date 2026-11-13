using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Input;
using Em.Grid;
using Em.Editors;
using Em.Grid.Menus;
using Em.Grid.Query;
using Em.Ui.Wpf.Documents.Editors;
using Em.Ui.Wpf.Documents.Themes;
using Em.Ui.Wpf.Documents.Menus;

namespace Em.Ui.Wpf.Documents.Grid;

public sealed partial class MatrixGrid
{
    private Canvas? _autoFilter;
    private ScrollViewer? _autoFilterScroll;
    private Border? _filterBar;
    private TextBlock? _filterDescription;
    private CheckBox? _filterEnabled;
    private bool _syncingFilter;
    private Popup? _columnFilterPopup;
    private readonly Dictionary<string, FrameworkElement> _filterEditors = new(StringComparer.Ordinal);

    private void BuildFilterSurfaces()
    {
        _filterEditors.Clear();
        _autoFilter = new Canvas { Height = 44, HorizontalAlignment = HorizontalAlignment.Left };
        _autoFilterScroll = new ScrollViewer
        {
            Content = _autoFilter, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false
        };
        _autoFilterScroll.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLowest");

        _filterEnabled = new CheckBox { Style = Material.Style("CheckBox"), VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(_filterEnabled, "Enable filter");
        _filterEnabled.Checked += (_, _) => SetFilterEnabled(true);
        _filterEnabled.Unchecked += (_, _) => SetFilterEnabled(false);
        _filterDescription = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _filterDescription.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurface");
        var clear = Material.IconButton("FilterClear", "Clear filters");
        clear.Click += (_, _) => SetCurrentValue(QueryProperty, Query with { Filter = null });
        var edit = Material.Button("Edit filter");
        edit.Margin = new Thickness(4, 0, 4, 0);
        edit.Click += (_, _) => OpenFilterEditor();
        var row = new DockPanel();
        var icon = Material.Icon("FilterActive");
        icon.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(_filterEnabled, Dock.Left);
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(edit, Dock.Right);
        row.Children.Add(_filterEnabled);
        row.Children.Add(icon);
        row.Children.Add(clear);
        row.Children.Add(edit);
        row.Children.Add(_filterDescription);
        _filterBar = new Border { Height = 44, Padding = new Thickness(12, 0, 8, 0), Child = row };
        _filterBar.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainer");
        AttachGridMenu(_filterBar, () => new MenuHit(MenuRegion.FilterPanel));
    }

    private void SetFilterEnabled(bool enabled)
    {
        if (!_syncingFilter)
            SetCurrentValue(QueryProperty, Query with { FilterEnabled = enabled });
    }

    private void AddFilterSurfaces(DockPanel surface)
    {
        DockPanel.SetDock(_autoFilterScroll!, Dock.Top);
        DockPanel.SetDock(_filterBar!, Dock.Bottom);
        surface.Children.Add(_autoFilterScroll!);
        surface.Children.Add(_filterBar!);
    }

    private void RebuildFilterSurfaces()
    {
        if (_autoFilterScroll is null || _autoFilter is null)
            return;
        _autoFilterScroll.Visibility = ShowAutoFilter ? Visibility.Visible : Visibility.Collapsed;
        _autoFilter.Children.Clear();
        _filterEditors.Clear();
        ScrollFilterRow();
        UpdateFilterPanel();
    }

    private void ScrollFilterRow()
    {
        if (_autoFilterScroll is null || _autoFilter is null || !ShowAutoFilter)
            return;
        _autoFilter.Width = SurfaceWidth;
        var visible = RenderColumns().ToArray();
        var needed = visible.Select(item => item.Column.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in _filterEditors.Keys.Where(key => !needed.Contains(key)).ToArray())
        {
            _autoFilter.Children.Remove(_filterEditors[key]);
            _filterEditors.Remove(key);
        }

        foreach (var (column, left, width) in visible)
        {
            if (!_filterEditors.TryGetValue(column.Key, out var field))
            {
                field = CreateAutoFilterField(column);
                _filterEditors.Add(column.Key, field);
                _autoFilter.Children.Add(field);
            }
            field.Width = Math.Max(44, width - 8);
            Panel.SetZIndex(field, column.IsFrozen ? 1 : 0);
            Canvas.SetLeft(field, left + 4);
            Canvas.SetTop(field, 7);
        }
        if (ViewportWidth > 0 && double.IsFinite(ViewportWidth))
        {
            _autoFilterScroll.Width = ViewportWidth;
            _autoFilterScroll.HorizontalAlignment = HorizontalAlignment.Left;
        }
        _autoFilterScroll.ScrollToHorizontalOffset(HorizontalOffset);
    }

    private FrameworkElement CreateAutoFilterField(GridColumn column)
    {
        var existing = FilterTree.ForColumn(Query.Filter, column.Key) as FilterCondition;
        var ops = AvailableOperators(column.Kind).Where(op => op is not (FilterOperator.Between or FilterOperator.In)).ToArray();
        var selected = existing?.Operator ?? (column.Kind == GridValueKind.Text ? FilterOperator.Contains : FilterOperator.Equal);
        if (!ops.Contains(selected))
            selected = ops[0];
        var operation = Material.Button(OperatorLabel(selected), "OperatorButton");
        AutomationProperties.SetName(operation, $"Filter operator for {column.Header}");
        if (existing is not null)
            Material.Paint(operation, BackgroundProperty, "SecondaryContainer");

        var settings = (column.Editor ?? DefaultEditor(column.Kind)) with { AllowNull = true, IsReadOnly = false, Validate = null };
        var editor = EditorFactory.Create(settings);
        editor.EditValue = existing is null ? null : ObjectRows.Unwrap(existing.Operand);
        editor.Element.MinHeight = 0;
        editor.Element.Height = 30;
        AutomationProperties.SetName(editor.Element, $"Filter {column.Header}");

        void Apply(object? value)
        {
            var expression = selected is FilterOperator.IsNull or FilterOperator.IsBlank
                ? new FilterCondition(column.Key, selected)
                : value is null || value is string { Length: 0 }
                    ? null : new FilterCondition(column.Key, selected, ObjectRows.ToValue(value));
            SetColumnFilter(column.Key, expression);
        }

        editor.Host = new AutoFilterHost(Apply, () => { });
        var menu = MenuRenderer.Attach(operation, () => new MenuOpeningEventArgs(new MenuHit(MenuRegion.FilterPanel, column.Key),
            ops.Select<FilterOperator, MenuEntry>(op => new MenuCommand(op.ToString(), OperatorName(op),
                "Filter", IsChecked: selected == op))
                .Prepend(new MenuSection($"{column.Header} · operators"))
                .Append(new MenuSeparator())
                .Append(new MenuCommand("clear", "Clear this filter", "FilterClear", IsEnabled: existing is not null))
                .ToImmutableArray()), id => new DocumentCommand(() =>
                {
                    if (id == "clear")
                        SetColumnFilter(column.Key, null);
                    else if (Enum.TryParse<FilterOperator>(id, out var op))
                    {
                        selected = op;
                        operation.Content = OperatorLabel(op);
                        if (op is FilterOperator.IsNull or FilterOperator.IsBlank || editor.EditValue is not null)
                            Apply(editor.EditValue);
                    }
                }, () => true));
        TrackGridMenu(menu);
        menu.Placement = PlacementMode.Bottom;
        operation.Click += (_, _) => menu.IsOpen = true;
        var field = new DockPanel { Height = 30 };
        DockPanel.SetDock(operation, Dock.Left);
        field.Children.Add(operation);
        field.Children.Add(editor.Element);
        return field;
    }

    private void UpdateFilterPanel()
    {
        if (_filterBar is null || _filterDescription is null || _filterEnabled is null)
            return;
        _filterBar.Visibility = Query.Filter is null ? Visibility.Collapsed : Visibility.Visible;
        _filterDescription.Text = Query.Filter is { } filter ? FilterTree.Describe(filter) : string.Empty;
        _syncingFilter = true;
        _filterEnabled.IsChecked = Query.FilterEnabled;
        _syncingFilter = false;
    }

    /// <summary>Opens the filter editor for the whole filter tree.</summary>
    public void OpenFilterEditor()
    {
        var columns = Columns.Count != 0 ? Columns.ToArray() : _generatedColumns;
        if (columns.Length == 0)
            return;
        var editor = new FilterTreeControl(columns, Query.Filter);
        var outer = new DockPanel();
        var actions = new DockPanel { Margin = new Thickness(0, 16, 0, 0), LastChildFill = false };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        Material.Paint(error, TextBlock.ForegroundProperty, "Error");
        var clear = Material.Button("Clear all");
        var cancel = Material.Button("Cancel");
        cancel.IsCancel = true;
        var apply = Material.Button("Apply");
        var ok = Material.Button("OK", "FilledButton");
        ok.IsDefault = true;
        var confirmation = new StackPanel { Orientation = Orientation.Horizontal };
        apply.Margin = new Thickness(8, 0, 0, 0);
        ok.Margin = new Thickness(8, 0, 0, 0);
        confirmation.Children.Add(cancel);
        confirmation.Children.Add(apply);
        confirmation.Children.Add(ok);
        DockPanel.SetDock(confirmation, Dock.Right);
        actions.Children.Add(clear);
        actions.Children.Add(confirmation);
        DockPanel.SetDock(actions, Dock.Bottom);
        outer.Children.Add(actions);
        var expression = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var preview = new Border { Child = expression, Padding = new Thickness(12), Margin = new Thickness(0, 16, 0, 0),
            CornerRadius = new CornerRadius(4) };
        Material.Paint(preview, BackgroundProperty, "SurfaceContainerHighest");
        DockPanel.SetDock(error, Dock.Bottom);
        DockPanel.SetDock(preview, Dock.Bottom);
        outer.Children.Add(error);
        outer.Children.Add(preview);
        outer.Children.Add(new ScrollViewer { Content = editor, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var dialog = CreateDialog("Filter editor", outer, Math.Min(904, SystemParameters.WorkArea.Width));

        void ShowExpression()
        {
            try
            {
                expression.Text = editor.Expression() is { } filter ? FilterTree.Describe(filter) : "No filter conditions";
                error.Text = string.Empty;
            }
            catch (FormatException)
            {
                expression.Text = "Complete the filter conditions to preview the expression.";
            }
        }
        void ApplyFilter(bool close)
        {
            try
            {
                SetCurrentValue(QueryProperty, Query with { Filter = editor.Expression(), FilterEnabled = true });
                error.Text = string.Empty;
                if (close)
                    dialog.DialogResult = true;
            }
            catch (FormatException ex) { error.Text = ex.Message; }
        }
        editor.Changed += (_, _) => ShowExpression();
        clear.Click += (_, _) => editor.Clear();
        apply.Click += (_, _) => ApplyFilter(false);
        ok.Click += (_, _) => ApplyFilter(true);
        ShowExpression();
        dialog.ShowDialog();
    }

    /// <summary>Opens the header filter popup of a column.</summary>
    /// <param name="key">The column key.</param>
    public void OpenColumnFilter(string key) => OpenColumnFilter(key, _headers.GetValueOrDefault(key) ?? this);

    private void OpenColumnFilter(string key, FrameworkElement anchor)
    {
        if (GetColumn(key) is not { } column || (Provider ?? _local) is not { } provider)
            return;
        if (_columnFilterPopup is { } previousPopup)
            previousPopup.IsOpen = false;

        var outer = new DockPanel { Width = 340 };
        var bodyPadding = new Thickness(16, 12, 16, 8);
        const double bodyHeight = 276;
        var actions = new DockPanel { Height = 56, Margin = new Thickness(12, 0, 12, 0), LastChildFill = false };
        var apply = Material.Button("OK", "FilledButton");
        apply.Margin = new Thickness(8, 0, 0, 0);
        var clear = Material.Button("Clear");
        var cancel = Material.Button("Cancel");
        DockPanel.SetDock(apply, Dock.Right);
        DockPanel.SetDock(cancel, Dock.Right);
        actions.Children.Add(clear);
        actions.Children.Add(apply);
        actions.Children.Add(cancel);
        DockPanel.SetDock(actions, Dock.Bottom);
        outer.Children.Add(actions);
        var error = new TextBlock { Margin = new Thickness(16, 0, 16, 0), TextWrapping = TextWrapping.Wrap };
        Material.Paint(error, TextBlock.ForegroundProperty, "Error");
        DockPanel.SetDock(error, Dock.Bottom);
        outer.Children.Add(error);

        var initial = FilterTree.ForColumn(Query.Filter, key);
        var inclusion = initial as FilterCondition;
        var exclusion = (initial as NegatedFilter)?.Term as FilterCondition;
        bool allSelected = inclusion?.Operator != FilterOperator.In;
        var selected = (allSelected ? exclusion : inclusion) is { Operator: FilterOperator.In } listFilter
            ? listFilter.Operands.ToHashSet() : new HashSet<GridValue>();
        var status = new TextBlock
        {
            FontSize = 12, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Material.Paint(status, TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var search = new TextBox
        {
            Style = Material.Style("SearchField"), ToolTip = "Search values", Tag = "Search values",
            FocusVisualStyle = null, Margin = new Thickness(0, 0, 0, 8)
        };
        AutomationProperties.SetName(search, "Search filter values");
        var items = new StackPanel();
        var values = new DockPanel { Margin = bodyPadding };
        DockPanel.SetDock(search, Dock.Top);
        values.Children.Add(search);
        var selectAll = new CheckBox { Content = "(Select all)", Height = 36, Padding = new Thickness(4, 0, 4, 0),
            IsChecked = selected.Count == 0 ? allSelected : null };
        DockPanel.SetDock(selectAll, Dock.Top);
        values.Children.Add(selectAll);
        var paging = new DockPanel { LastChildFill = true };
        var previous = Material.IconButton("ChevronLeft", "Previous values page");
        var next = Material.IconButton("ChevronRight", "Next values page");
        DockPanel.SetDock(previous, Dock.Left);
        DockPanel.SetDock(next, Dock.Right);
        paging.Children.Add(previous);
        paging.Children.Add(next);
        paging.Children.Add(status);
        DockPanel.SetDock(paging, Dock.Bottom);
        values.Children.Add(paging);
        var valueList = new ScrollViewer
        {
            Content = items, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        values.Children.Add(valueList);

        bool startsWithValues = initial is null || inclusion?.Operator == FilterOperator.In || exclusion?.Operator == FilterOperator.In;
        var custom = new ColumnFilterConditions(column, startsWithValues ? null : initial);
        custom.Margin = bodyPadding;
        var tabs = new TabControl();
        tabs.Items.Add(CreateTab("Values", new Border { Child = values, Height = bodyHeight }));
        tabs.Items.Add(CreateTab("Custom", new ScrollViewer
        {
            Content = custom, Height = bodyHeight, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        }));
        tabs.SelectedIndex = startsWithValues ? 0 : 1;
        outer.Children.Add(tabs);
        var card = Material.Panel(outer, "SurfaceContainer", 12, new Thickness(0));
        card.Margin = new Thickness(0, 0, 10, 10);
        Material.Scope(card);
        var popup = _columnFilterPopup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, Child = card, StaysOpen = false
        };
        GridTheme.SetMode(card, GridTheme.GetMode(this));

        static TabItem CreateTab(string header, FrameworkElement content) => new()
        {
            Header = header, Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };

        const int size = 150;
        int offset = 0;
        CancellationTokenSource? request = null;
        async void RefreshValues(int page)
        {
            request?.Cancel();
            var pending = request = new CancellationTokenSource();
            var token = pending.Token;
            try
            {
                items.Children.Clear();
                valueList.ScrollToTop();
                paging.Visibility = Visibility.Visible;
                status.Text = "Loading…";
                previous.IsEnabled = next.IsEnabled = false;
                var existingFilter = Query.FilterEnabled ? FilterTree.ReplaceColumn(Query.Filter, key, null) : null;
                FilterExpression? searchFilter = null;
                if (search.Text.Length > 0)
                    searchFilter = new FilterCondition(key, FilterOperator.Contains, GridValue.From(search.Text))
                    {
                        UseDisplayText = true, DisplayFormat = column.Editor?.DisplayFormat, DisplayLabels = DisplayLabelsFor(column),
                        CultureName = CultureInfo.CurrentCulture.Name
                    };
                var combined = searchFilter is null ? existingFilter :
                    existingFilter is null ? searchFilter : new AllFilters([existingFilter, searchFilter]);
                var withoutColumn = EffectiveQuery with
                {
                    Filter = combined, FilterEnabled = true,
                    Groups = [], Summaries = [], Sort = []
                };
                var range = new global::Em.Grid.Data.RowRange(page, size + 1);
                var pageValues = provider is global::Em.Grid.Data.IDistinctValueCountsProvider withCounts
                    ? (await withCounts.GetDistinctValueCountsAsync(withoutColumn, key, range, token))
                        .Select(entry => (entry.Value, Count: (int?)entry.Count)).ToArray()
                    : (await provider.GetDistinctValuesAsync(withoutColumn, key, range, token))
                        .Select(value => (Value: value, Count: (int?)null)).ToArray();
                if (!ReferenceEquals(request, pending))
                    return;
                offset = page;
                previous.IsEnabled = page > 0;
                next.IsEnabled = pageValues.Length > size;
                foreach (var (value, count) in pageValues.Take(size))
                {
                    var label = value.Kind == GridValueKind.Null ? "(Blanks)" : column.Editor?.Format(ObjectRows.Unwrap(value)) ?? value.ToString();
                    var valueRow = new DockPanel();
                    if (count is { } number)
                    {
                        var total = new TextBlock
                        {
                            Text = number.ToString("N0", CultureInfo.CurrentCulture), FontSize = 12, FontWeight = FontWeights.Medium,
                            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
                        };
                        Material.Paint(total, TextBlock.ForegroundProperty, "OnSurfaceVariant");
                        DockPanel.SetDock(total, Dock.Right);
                        valueRow.Children.Add(total);
                    }
                    valueRow.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis });
                    var check = new CheckBox
                    {
                        Content = valueRow, IsChecked = allSelected != selected.Contains(value), Height = 36,
                        Padding = new Thickness(4, 0, 4, 0), ToolTip = label
                    };
                    AutomationProperties.SetName(check, label);
                    check.Click += (_, _) =>
                    {
                        if (allSelected != (check.IsChecked == true))
                            selected.Add(value);
                        else
                            selected.Remove(value);
                        selectAll.IsChecked = selected.Count == 0 ? allSelected : null;
                    };
                    items.Children.Add(check);
                }
                status.Text = items.Children.Count == 0 ? "No values" : $"{page + 1}–{page + items.Children.Count}";
                paging.Visibility = previous.IsEnabled || next.IsEnabled || items.Children.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { if (ReferenceEquals(request, pending)) status.Text = ex.GetBaseException().Message; }
            finally
            {
                if (ReferenceEquals(request, pending))
                    request = null;
                pending.Dispose();
            }
        }

        previous.Click += (_, _) => RefreshValues(Math.Max(0, offset - size));
        next.Click += (_, _) => RefreshValues(offset + size);
        search.TextChanged += (_, _) => RefreshValues(0);
        selectAll.Click += (_, _) =>
        {
            allSelected = !allSelected || selected.Count > 0;
            selected.Clear();
            selectAll.IsChecked = allSelected;
            RefreshValues(offset);
        };
        popup.Opened += (_, _) =>
        {
            RefreshValues(0);
            if (tabs.SelectedIndex == 0)
                search.Focus();
        };
        popup.Closed += (_, _) =>
        {
            var pending = request;
            request = null;
            pending?.Cancel();
            anchor.Unloaded -= CloseWithAnchor;
            if (ReferenceEquals(_columnFilterPopup, popup))
                _columnFilterPopup = null;
        };
        anchor.Unloaded += CloseWithAnchor;
        void CloseWithAnchor(object sender, RoutedEventArgs e) => popup.IsOpen = false;
        void ClosePopup()
        {
            popup.IsOpen = false;
            if (!anchor.Focus())
                Focus();
        }
        void Confirm()
        {
            try
            {
                FilterExpression? expression;
                if (tabs.SelectedIndex == 0)
                {
                    var set = new FilterCondition(key, FilterOperator.In) { Operands = selected.ToImmutableArray() };
                    expression = allSelected ? selected.Count == 0 ? null : new NegatedFilter(set) : set;
                }
                else
                    expression = custom.Expression();
                ClosePopup();
                SetColumnFilter(key, expression);
            }
            catch (FormatException ex) { error.Text = ex.Message; }
        }
        outer.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                ClosePopup();
            else if (e.Key == Key.Enter && e.OriginalSource == search)
                Confirm();
            else
                return;
            e.Handled = true;
        };
        apply.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => ClosePopup();
        clear.Click += (_, _) => { ClosePopup(); SetColumnFilter(key, null); };
        popup.IsOpen = true;
    }

    internal static IEnumerable<FilterOperator> AvailableOperators(GridValueKind kind)
    {
        yield return FilterOperator.Equal;
        yield return FilterOperator.NotEqual;
        if (kind == GridValueKind.Text)
        {
            yield return FilterOperator.Contains;
            yield return FilterOperator.StartsWith;
            yield return FilterOperator.EndsWith;
            yield return FilterOperator.IsBlank;
        }
        if (kind is GridValueKind.Date or GridValueKind.Number)
        {
            yield return FilterOperator.Greater;
            yield return FilterOperator.GreaterOrEqual;
            yield return FilterOperator.Less;
            yield return FilterOperator.LessOrEqual;
            yield return FilterOperator.Between;
        }
        yield return FilterOperator.IsNull;
        yield return FilterOperator.In;
    }

    private static string OperatorLabel(FilterOperator op) => op switch
    {
        FilterOperator.Equal => "=", FilterOperator.NotEqual => "≠", FilterOperator.Greater => ">",
        FilterOperator.GreaterOrEqual => "≥", FilterOperator.Less => "<", FilterOperator.LessOrEqual => "≤",
        FilterOperator.Contains => "∋", FilterOperator.StartsWith => "⇤", FilterOperator.EndsWith => "⇥",
        FilterOperator.IsNull or FilterOperator.IsBlank => "∅", _ => op.ToString()
    };

    internal static string OperatorName(FilterOperator operation) => operation switch
    {
        FilterOperator.Equal => "Equals",
        FilterOperator.NotEqual => "Does not equal",
        FilterOperator.Greater => "Is greater than",
        FilterOperator.GreaterOrEqual => "Is greater than or equal to",
        FilterOperator.Less => "Is less than",
        FilterOperator.LessOrEqual => "Is less than or equal to",
        FilterOperator.StartsWith => "Begins with",
        FilterOperator.EndsWith => "Ends with",
        FilterOperator.IsNull => "Is null",
        FilterOperator.IsBlank => "Is blank",
        _ => operation.ToString()
    };

    private sealed class ColumnFilterConditions : StackPanel
    {
        private readonly Func<FilterExpression?> _read;

        public ColumnFilterConditions(GridColumn column, FilterExpression? initial)
        {
            var operators = AvailableOperators(column.Kind)
                .Where(operation => operation is not (FilterOperator.Between or FilterOperator.In)).ToArray();
            FilterExpression[] terms = initial switch
            {
                AllFilters all => all.Terms.ToArray(),
                AnyFilter any => any.Terms.ToArray(),
                null => [],
                _ => [initial]
            };
            if (terms.Length > 2 || terms.Any(term => term is not FilterCondition condition
                || !operators.Contains(condition.Operator) || condition.UseDisplayText))
            {
                var advanced = new FilterTreeControl([column], initial);
                Children.Add(advanced);
                _read = advanced.Expression;
                return;
            }

            Children.Add(new TextBlock { Text = $"Show rows where {column.Header}", FontWeight = FontWeights.Medium, MinHeight = 20,
                Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            var first = AddCondition(terms.ElementAtOrDefault(0) as FilterCondition);
            var mode = new ListBox { ItemsSource = new[] { "And", "Or" }, SelectedIndex = initial is AnyFilter ? 1 : 0,
                Style = Material.Style("Segments"), Margin = new Thickness(0, 8, 0, 8) };
            AutomationProperties.SetName(mode, "Join conditions");
            Children.Add(mode);
            var second = AddCondition(terms.ElementAtOrDefault(1) as FilterCondition);
            _read = () =>
            {
                var left = first();
                var right = second();
                if (left is null || right is null)
                    return left ?? right;
                return mode.SelectedIndex == 1 ? new AnyFilter([left, right]) : new AllFilters([left, right]);
            };

            Func<FilterCondition?> AddCondition(FilterCondition? condition)
            {
                var choice = new ComboBox
                {
                    ItemsSource = operators.Select(op => new OperatorOption(op, OperatorName(op))).ToArray(),
                    DisplayMemberPath = nameof(OperatorOption.Label), SelectedValuePath = nameof(OperatorOption.Operation),
                    SelectedValue = condition?.Operator ?? (column.Kind == GridValueKind.Text ? FilterOperator.Contains : FilterOperator.Equal)
                };
                AutomationProperties.SetName(choice, "Condition operator");
                Children.Add(choice);
                var settings = (column.Editor ?? DefaultEditor(column.Kind)) with { AllowNull = true, IsReadOnly = false, Validate = null };
                var editor = EditorFactory.Create(settings);
                if (condition is not null)
                    editor.EditValue = ObjectRows.Unwrap(condition.Operand);
                editor.Element.Margin = new Thickness(0, 8, 0, 0);
                AutomationProperties.SetName(editor.Element, $"Condition value for {column.Header}");
                Children.Add(editor.Element);
                choice.SelectionChanged += (_, _) => EnableValue();
                EnableValue();

                void EnableValue() => editor.Element.IsEnabled = choice.SelectedValue is not (FilterOperator.IsNull or FilterOperator.IsBlank);

                return () =>
                {
                    var operation = choice.SelectedValue is FilterOperator selected ? selected : FilterOperator.Equal;
                    if (!editor.Element.IsEnabled)
                        return new FilterCondition(column.Key, operation);
                    if (!editor.Commit())
                        throw new FormatException(editor.Error ?? $"Enter a valid value for {column.Header}.");
                    return editor.EditValue is null or string { Length: 0 }
                        ? null : new FilterCondition(column.Key, operation, ObjectRows.ToValue(editor.EditValue));
                };
            }
        }

        public FilterExpression? Expression() => _read();

        private sealed record OperatorOption(FilterOperator Operation, string Label);
    }

    private sealed class AutoFilterHost(Action<object?> apply, Action clear) : IInPlaceEditorHost
    {
        public void Commit(object? value) => apply(value);
        public void Cancel() => clear();
        public bool Navigate(Key key, ModifierKeys modifiers) => false;
    }
}
