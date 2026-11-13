using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;
using Em.Controls.Grid.Menus;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixGrid
{
    private const string GroupDragFormat = "MatrixGrid.GroupKey";
    private const string ColumnChooserDragFormat = "MatrixGrid.ColumnChooserKey";

    /// <summary>Identifies the <see cref="ShowGroupPanel"/> dependency property.</summary>
    public static readonly DependencyProperty ShowGroupPanelProperty = DependencyProperty.Register(
        nameof(ShowGroupPanel), typeof(bool), typeof(MatrixGrid), new PropertyMetadata(true, OperationVisibilityChanged));

    /// <summary>Identifies the <see cref="ShowAutoFilter"/> dependency property.</summary>
    public static readonly DependencyProperty ShowAutoFilterProperty = DependencyProperty.Register(
        nameof(ShowAutoFilter), typeof(bool), typeof(MatrixGrid), new PropertyMetadata(false, OperationVisibilityChanged));

    /// <summary>Identifies the <see cref="MenuOpeningCommand"/> dependency property.</summary>
    public static readonly DependencyProperty MenuOpeningCommandProperty = DependencyProperty.Register(
        nameof(MenuOpeningCommand), typeof(ICommand), typeof(MatrixGrid));

    private StackPanel? _groupPanel;
    private Border? _groupBar;
    private Canvas? _dropOverlay;
    private Canvas? _footer;
    private ScrollViewer? _footerScroll;
    private ImmutableDictionary<string, GridValue> _totals = ImmutableDictionary<string, GridValue>.Empty;
    private string? _footerColumn;
    private bool _updatingColumns;
    private TextBox? _columnChooser;
    private readonly List<FilterExpression> _recentFilters = [];

    /// <summary>Gets or sets whether the group panel is shown above the column headers.</summary>
    public bool ShowGroupPanel
    {
        get => (bool)GetValue(ShowGroupPanelProperty);
        set => SetValue(ShowGroupPanelProperty, value);
    }

    /// <summary>Gets or sets whether the auto-filter row is shown below the column headers.</summary>
    public bool ShowAutoFilter
    {
        get => (bool)GetValue(ShowAutoFilterProperty);
        set => SetValue(ShowAutoFilterProperty, value);
    }

    /// <summary>Gets or sets a command executed with <see cref="MenuOpeningEventArgs"/> before a context menu opens, for MVVM menu customization.</summary>
    public ICommand? MenuOpeningCommand
    {
        get => (ICommand?)GetValue(MenuOpeningCommandProperty);
        set => SetValue(MenuOpeningCommandProperty, value);
    }

    /// <summary>Gets the menu definitions that replace, extend or insert into the built-in context menus.</summary>
    public ObservableCollection<GridMenuDefinition> MenuDefinitions { get; } = [];
    /// <summary>Gets or sets a resolver that maps menu command identifiers to host commands.</summary>
    public Func<string, ICommand?>? MenuCommandResolver { get; set; }
    /// <summary>Occurs before a context menu opens, so handlers can change or cancel it.</summary>
    public event EventHandler<MenuOpeningEventArgs>? MenuOpening;
    /// <summary>Occurs when the user asks for the print preview.</summary>
    public event EventHandler? PrintPreviewRequested;
    /// <summary>Occurs when the user asks to export the view.</summary>
    public event EventHandler? ExportRequested;
    /// <summary>Gets the number of data rows that match the current query, or null when it is not known yet.</summary>
    public int? ResultRowCount { get; private set; }

    /// <summary>Captures the column layout, the query and the panel options.</summary>
    /// <param name="includeSearch">Whether the active finder search is stored.</param>
    /// <returns>The layout.</returns>
    public GridLayout CaptureLayout(bool includeSearch = true)
    {
        var columns = Columns.Count > 0 ? Columns.ToArray() : _generatedColumns;
        return new GridLayout
        {
            Columns = columns.Select(column => new GridColumnLayout(column.Key, WidthFor(column), column.IsVisible, column.IsFrozen, column.Band))
                .ToImmutableArray(),
            Query = includeSearch ? Query : Query with { Search = null },
            FinderSearch = includeSearch && _finderCriteria is { } search ? search with { DisplayFormatter = null } : null,
            ShowGroupPanel = ShowGroupPanel,
            ShowAutoFilter = ShowAutoFilter,
            ShowSearch = includeSearch && IsSearchPanelVisible
        };
    }

    /// <summary>Restores a layout captured by <see cref="CaptureLayout(bool)"/>. Columns that no longer exist are ignored.</summary>
    /// <param name="layout">The layout.</param>
    public void RestoreLayout(GridLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        EnsureExplicitColumns();
        var available = Columns.DistinctBy(column => column.Key).ToDictionary(column => column.Key, StringComparer.Ordinal);
        var result = new List<GridColumn>();
        foreach (var setting in layout.Columns)
        {
            if (available.Remove(setting.Key, out var column))
                result.Add(column with { Width = WidthFor(column with { Width = setting.Width }), IsVisible = setting.IsVisible, IsFrozen = setting.IsFrozen, Band = setting.Band });
        }
        result.AddRange(available.Values);
        Columns.Clear();
        foreach (var column in result)
            Columns.Add(column);
        SetCurrentValue(ShowGroupPanelProperty, layout.ShowGroupPanel);
        SetCurrentValue(ShowAutoFilterProperty, layout.ShowAutoFilter);
        SetCurrentValue(IsSearchPanelVisibleProperty, false);
        SetCurrentValue(QueryProperty, layout.Query);
        if (layout.ShowSearch)
        {
            ShowSearch();
            if (layout.FinderSearch is { } search)
                SeedFinderFrom(search);
        }
    }

    /// <summary>Groups the view by a column.</summary>
    /// <param name="columnKey">The column to group by.</param>
    /// <param name="position">The zero-based position among the group columns, or null to add it last.</param>
    public void GroupBy(string columnKey, int? position = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);
        if (GetColumn(columnKey) is null)
            return;
        var groups = Query.Groups.ToBuilder();
        int existing = GroupIndex(columnKey);
        var item = existing >= 0 ? groups[existing] : new GroupDescriptor(columnKey);
        if (existing >= 0)
            groups.RemoveAt(existing);
        groups.Insert(Math.Clamp(position ?? groups.Count, 0, groups.Count), item);
        ChangeGroups(groups.ToImmutable());
    }

    /// <summary>Removes a column from the grouping.</summary>
    /// <param name="columnKey">The column.</param>
    public void Ungroup(string columnKey) =>
        ChangeGroups(Query.Groups.Where(group => group.ColumnKey != columnKey).ToImmutableArray());

    /// <summary>Reverses the group order of a column.</summary>
    /// <param name="columnKey">The group column.</param>
    public void ToggleGroupSort(string columnKey)
    {
        int index = GroupIndex(columnKey);
        if (index >= 0)
        {
            var group = Query.Groups[index];
            SetCurrentValue(QueryProperty, Query with
            {
                Groups = Query.Groups.SetItem(index, group with { Descending = !group.Descending })
            });
        }
    }

    /// <summary>Expands or collapses a group.</summary>
    /// <param name="group">The group row information.</param>
    public void ToggleGroup(GroupRowInfo group)
    {
        if (group.IsFooter)
            return;
        var collapsed = Query.CollapsedGroups;
        var expanded = Query.ExpandedGroups;
        if (group.IsExpanded)
        {
            collapsed = collapsed.Add(group.Key);
            expanded = expanded.Remove(group.Key);
        }
        else
        {
            collapsed = collapsed.Remove(group.Key);
            expanded = expanded.Add(group.Key);
        }
        SetCurrentValue(QueryProperty, Query with { CollapsedGroups = collapsed, ExpandedGroups = expanded });
    }

    /// <summary>Expands or collapses every group.</summary>
    /// <param name="expand">True to expand, false to collapse.</param>
    public void ExpandGroups(bool expand) => SetCurrentValue(QueryProperty, Query with
    {
        ExpandGroupsByDefault = expand,
        CollapsedGroups = [],
        ExpandedGroups = []
    });

    /// <summary>Adds or removes a summary on a column.</summary>
    /// <param name="columnKey">The column.</param>
    /// <param name="scope">Whether the summary is a total or a group footer.</param>
    /// <param name="operation">The aggregate.</param>
    public void ToggleSummary(string columnKey, SummaryScope scope, SummaryOperation operation)
    {
        if (GetColumn(columnKey) is not { } column || !SummaryRules.Allows(column.Kind, operation))
            return;
        var descriptor = new SummaryDescriptor(columnKey, operation, scope);
        var summaries = Query.Summaries;
        SetCurrentValue(QueryProperty, Query with
        {
            Summaries = summaries.Contains(descriptor) ? summaries.Remove(descriptor) : summaries.Add(descriptor)
        });
    }

    /// <summary>Removes every summary of a column in one scope.</summary>
    /// <param name="columnKey">The column.</param>
    /// <param name="scope">The summary scope.</param>
    public void ClearSummaries(string columnKey, SummaryScope scope) =>
        SetCurrentValue(QueryProperty, Query with
        {
            Summaries = Query.Summaries.Where(item => item.ColumnKey != columnKey || item.Scope != scope).ToImmutableArray()
        });

    /// <summary>Replaces the filter of one column and turns filtering on.</summary>
    /// <param name="columnKey">The column.</param>
    /// <param name="expression">The new filter, or null to remove it.</param>
    public void SetColumnFilter(string columnKey, FilterExpression? expression) =>
        SetCurrentValue(QueryProperty, Query with
        {
            Filter = FilterTree.ReplaceColumn(Query.Filter, columnKey, expression),
            FilterEnabled = true
        });

    /// <summary>Sizes a column to fit its header and the loaded cell text.</summary>
    /// <param name="key">The column key.</param>
    /// <returns>True when the column exists.</returns>
    public bool BestFitColumn(string key)
    {
        if (GetColumn(key) is not { } column)
            return false;
        return ResizeColumn(key, BestFitWidth(column, BestFitRows()));
    }

    /// <summary>Sizes every visible column to fit its header and the loaded cell text.</summary>
    public void BestFitAllColumns()
    {
        var rows = BestFitRows();
        var widths = _displayColumns.ToDictionary(column => column.Key, column => BestFitWidth(column, rows));
        if (widths.Count == 0)
            return;

        _updatingColumns = true;
        try
        {
            EnsureExplicitColumns();
            for (int index = 0; index < Columns.Count; index++)
            {
                var column = Columns[index];
                if (widths.TryGetValue(column.Key, out var width) && column.Width != width)
                    Columns[index] = column with { Width = width };
            }
        }
        finally
        {
            _updatingColumns = false;
            RebuildColumns();
        }
    }

    private GridRow[] BestFitRows() => VisibleRows.Where(row => row.Group is null).Take(500).ToArray();

    private double BestFitWidth(GridColumn column, IReadOnlyList<GridRow> rows)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var captionFace = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var cellFace = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var padding = HeaderPadding(column, showFilter: true);
        double width = Measure(column.Header, FontSize, captionFace, TextOptions.GetTextFormattingMode(this))
            + SortIconWidth + HeaderGap + padding.Left + padding.Right;
        var rank = HeaderSortRank(SortIndex(column.Key));
        if (rank.Length != 0)
            width += Measure(rank, 11, captionFace, TextOptions.GetTextFormattingMode(this));
        foreach (var row in rows)
            width = Math.Max(width, Measure(Display(row, column), GridRowView.CellFontSize, cellFace, TextFormattingMode.Ideal)
                + 2 * GridRowView.CellPadding);
        return WidthFor(column with { Width = (Math.Ceiling(width * dpi) + 1) / dpi });

        double Measure(string text, double size, Typeface typeface, TextFormattingMode mode) =>
            new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection, typeface,
                size, Brushes.Black, null, mode, dpi).WidthIncludingTrailingWhitespace;
    }

    internal GridColumn? GetColumn(string key) =>
        Columns.FirstOrDefault(column => column.Key == key) ?? _generatedColumns.FirstOrDefault(column => column.Key == key);

    internal string SummaryText(IReadOnlyDictionary<string, GridValue> values, GridColumn column, SummaryScope scope) =>
        string.Join("  ·  ", Query.Summaries.Where(item => item.ColumnKey == column.Key && item.Scope == scope)
            .Where(item => values.ContainsKey(item.Key))
            .Select(item => $"{item.Operation} = {values[item.Key].Format(System.Globalization.CultureInfo.CurrentCulture,
                item.Operation is SummaryOperation.Count ? null : column.Editor?.DisplayFormat)}"));

    private int GroupIndex(string key)
    {
        for (int index = 0; index < Query.Groups.Length; index++)
            if (Query.Groups[index].ColumnKey == key)
                return index;
        return -1;
    }

    private void ChangeGroups(ImmutableArray<GroupDescriptor> groups) =>
        SetCurrentValue(QueryProperty, Query with { Groups = groups, CollapsedGroups = [], ExpandedGroups = [] });

    private Border BuildGroupBar()
    {
        _groupPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _dropOverlay = new Canvas { IsHitTestVisible = false };
        var layers = new System.Windows.Controls.Grid();
        layers.Children.Add(_groupPanel);
        layers.Children.Add(_dropOverlay);
        _groupBar = new Border
        {
            Height = 52, Padding = new Thickness(16, 0, 16, 0), Child = layers, AllowDrop = true, Focusable = true
        };
        AutomationProperties.SetName(_groupBar, "Grouping panel");
        _groupBar.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLow");
        _groupBar.DragOver += (_, e) =>
        {
            if (e.Data.GetData(ColumnDragFormat) is string key)
            {
                e.Effects = DragDropEffects.Move;
                DrawGroupDrop(key, e.GetPosition(_groupPanel).X);
            }
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        };
        _groupBar.DragLeave += (_, _) => _dropOverlay?.Children.Clear();
        _groupBar.Drop += (_, e) =>
        {
            _dropOverlay?.Children.Clear();
            if (e.Data.GetData(ColumnDragFormat) is not string key)
                return;
            double x = e.GetPosition(_groupPanel).X;
            int index = 0;
            foreach (FrameworkElement chip in _groupPanel.Children)
            {
                if (x < chip.ActualWidth / 2)
                    break;
                x -= chip.ActualWidth + chip.Margin.Left + chip.Margin.Right;
                index++;
            }
            int current = GroupIndex(key);
            if (current >= 0 && current < index)
                index--;
            GroupBy(key, index);
            e.Handled = true;
        };
        AttachGridMenu(_groupBar, () => new MenuHit(MenuRegion.GroupPanel));
        return _groupBar;
    }

    private void DrawGroupDrop(string key, double x)
    {
        if (_groupPanel is null || _dropOverlay is null)
            return;
        _dropOverlay.Children.Clear();
        double position = 0;
        if (Query.Groups.Length > 0)
            foreach (FrameworkElement chip in _groupPanel.Children)
            {
                if (x < position + chip.ActualWidth / 2)
                    break;
                position += chip.ActualWidth + chip.Margin.Left + chip.Margin.Right;
            }
        var indicator = new Border { Width = 3, Height = 32, CornerRadius = new CornerRadius(1) };
        indicator.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.Primary");
        Canvas.SetLeft(indicator, position);
        Canvas.SetTop(indicator, 10);
        _dropOverlay.Children.Add(indicator);
        var ghost = new Border
        {
            CornerRadius = new CornerRadius(8), Height = 32, Opacity = 0.90, Padding = new Thickness(9, 0, 9, 0),
            Child = new TextBlock { Text = GetColumn(key)?.Header ?? key, VerticalAlignment = VerticalAlignment.Center }
        };
        ghost.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerHigh");
        Canvas.SetLeft(ghost, Math.Max(6, Math.Min(x + 12, _dropOverlay.ActualWidth - 150)));
        Canvas.SetTop(ghost, 10);
        _dropOverlay.Children.Add(ghost);
    }

    private void RebuildGroupBar()
    {
        if (_groupBar is null || _groupPanel is null)
            return;
        _groupBar.Visibility = ShowGroupPanel ? Visibility.Visible : Visibility.Collapsed;
        _groupPanel.Children.Clear();
        if (Query.Groups.IsDefaultOrEmpty)
        {
            var icon = Material.Icon("Group");
            icon.Margin = new Thickness(0, 0, 8, 0);
            var hint = new TextBlock { Text = "Drag a column header here to group by that column",
                VerticalAlignment = VerticalAlignment.Center };
            hint.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurfaceVariant");
            _groupPanel.Children.Add(icon);
            _groupPanel.Children.Add(hint);
            return;
        }

        foreach (var group in Query.Groups)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Height = 32 };
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            var arrow = Material.Icon(group.Descending ? "ArrowDown" : "ArrowUp", 18, "OnSecondaryContainer");
            arrow.Margin = new Thickness(0, 0, 6, 0);
            label.Children.Add(arrow);
            label.Children.Add(new TextBlock { Text = GetColumn(group.ColumnKey)?.Header ?? group.ColumnKey,
                VerticalAlignment = VerticalAlignment.Center });
            var caption = new Button
            {
                Style = Material.Style("HeaderButton"), Content = label, Padding = new Thickness(8, 0, 5, 0)
            };
            AutomationProperties.SetName(caption, $"Sort {GetColumn(group.ColumnKey)?.Header ?? group.ColumnKey}");
            caption.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSecondaryContainer");
            caption.Click += (_, _) => ToggleGroupSort(group.ColumnKey);
            AttachGridMenu(caption, () => new MenuHit(MenuRegion.GroupPanel, group.ColumnKey));
            caption.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || !caption.IsPressed)
                    return;
                var payload = new DataObject(ColumnDragFormat, group.ColumnKey);
                payload.SetData(GroupDragFormat, group.ColumnKey);
                DragDrop.DoDragDrop(caption, payload, DragDropEffects.Move);
            };
            var remove = Material.IconButton("Close", $"Ungroup {group.ColumnKey}", 28);
            remove.Click += (_, _) => Ungroup(group.ColumnKey);
            chip.Children.Add(caption);
            chip.Children.Add(remove);
            var frame = new Border { Child = chip, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0) };
            Material.Paint(frame, BackgroundProperty, "SecondaryContainer");
            _groupPanel.Children.Add(frame);
        }
    }

    private ScrollViewer BuildTotalFooter()
    {
        _footer = new Canvas { Height = 40, HorizontalAlignment = HorizontalAlignment.Left };
        _footer.PreviewMouseRightButtonDown += (_, e) => _footerColumn = ColumnAt(e.GetPosition(_footer).X)?.Key;
        _footerScroll = new ScrollViewer
        {
            Content = _footer, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = true,
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(_footerScroll, "Total summaries");
        _footerScroll.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainer");
        AttachGridMenu(_footerScroll, () => new MenuHit(MenuRegion.TotalFooter,
            _footerColumn ?? State.FocusedCell?.ColumnKey ?? _displayColumns.FirstOrDefault()?.Key));
        _footerScroll.ContextMenu.Placement = PlacementMode.Top;
        _footerScroll.ContextMenu.Closed += (_, _) => _footerColumn = null;
        return _footerScroll;
    }

    private void RenderTotalFooter()
    {
        if (_footer is null || _footerScroll is null)
            return;
        _footerScroll.Visibility = _displayColumns.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _footer.Children.Clear();
        _footer.Width = SurfaceWidth;
        foreach (var (column, left, width) in RenderColumns())
        {
            var text = new TextBlock
            {
                Text = SummaryText(_totals, column, SummaryScope.Total), Width = width,
                Height = 40, Padding = new Thickness(12, 11, 12, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignmentFor(column)
            };
            text.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurface");
            if (column.IsFrozen)
                text.SetResourceReference(TextBlock.BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLow");
            Canvas.SetLeft(text, left);
            _footer.Children.Add(text);
        }
        if (_bodyScroll is { } scroll && scroll.ViewportWidth > 0 && double.IsFinite(scroll.ViewportWidth))
        {
            _footerScroll.Width = scroll.ViewportWidth;
            _footerScroll.HorizontalAlignment = HorizontalAlignment.Left;
            _footerScroll.ScrollToHorizontalOffset(scroll.HorizontalOffset);
        }
    }

    internal void AttachGridMenu(FrameworkElement target, Func<MenuHit> hit)
    {
        var menu = MenuRenderer.Attach(target, () =>
        {
            var context = hit();
            var opening = new MenuOpeningEventArgs(context, ComposeMenu(context));
            MenuOpening?.Invoke(this, opening);
            if (MenuOpeningCommand is { } command && command.CanExecute(opening))
                command.Execute(opening);
            return opening;
        }, id => MenuCommandResolver?.Invoke(id) ?? new GridMenuCommand(this, id));
        TrackGridMenu(menu);
    }

    internal static TextAlignment TextAlignmentFor(GridColumn column) => column.EffectiveAlignment switch
    {
        GridColumnAlignment.Right => TextAlignment.Right,
        GridColumnAlignment.Center => TextAlignment.Center,
        _ => TextAlignment.Left
    };

    internal static HorizontalAlignment HorizontalAlignmentFor(GridColumn column) => column.EffectiveAlignment switch
    {
        GridColumnAlignment.Right => HorizontalAlignment.Right,
        GridColumnAlignment.Center => HorizontalAlignment.Center,
        _ => HorizontalAlignment.Left
    };

    private ImmutableArray<MenuEntry> ComposeMenu(MenuHit context)
    {
        var definitions = MenuDefinitions.Where(candidate => candidate.Region == context.Region).ToArray();
        var replace = definitions.LastOrDefault(candidate => candidate.Mode == GridMenuMode.Replace);
        var items = replace is null ? DefaultMenu(context) : ResolveMenu(replace.Items);
        foreach (var definition in definitions)
        {
            if (definition.Mode == GridMenuMode.Replace || definition.Items.Count == 0)
                continue;
            var added = ResolveMenu(definition.Items);
            items = definition.Mode switch
            {
                GridMenuMode.Prepend => JoinMenus(added, items),
                GridMenuMode.Insert => items.InsertRange(InsertPosition(items, definition), added),
                _ => JoinMenus(items, added)
            };
        }

        return items;
    }

    private static ImmutableArray<MenuEntry> ResolveMenu(IEnumerable<MenuEntry> entries) =>
        [.. entries.Select(entry => entry is GridMenuSubmenu submenu
            ? new MenuSubmenu(submenu.Label, ResolveMenu(submenu.Items), submenu.Icon, submenu.Id)
            : entry)];

    private static int InsertPosition(ImmutableArray<MenuEntry> items, GridMenuDefinition definition)
    {
        bool before = !string.IsNullOrEmpty(definition.InsertBefore);
        var anchor = before ? definition.InsertBefore : definition.InsertAfter;
        if (!string.IsNullOrEmpty(anchor))
        {
            for (int index = 0; index < items.Length; index++)
            {
                if (items[index] is MenuCommand { CommandId: var id } && id == anchor
                    || items[index] is MenuSubmenu { Id: var submenuId } && submenuId == anchor)
                    return before ? index : index + 1;
            }
        }

        return Math.Clamp(definition.Index, 0, items.Length);
    }

    private static ImmutableArray<MenuEntry> JoinMenus(ImmutableArray<MenuEntry> first, ImmutableArray<MenuEntry> second)
    {
        if (first.IsDefaultOrEmpty)
            return second;
        if (second.IsDefaultOrEmpty)
            return first;
        var builder = first.ToBuilder();
        if (first[^1] is not MenuSeparator && second[0] is not MenuSeparator)
            builder.Add(new MenuSeparator());
        builder.AddRange(second);
        return builder.ToImmutable();
    }

    private ImmutableArray<MenuEntry> DefaultMenu(MenuHit hit)
    {
        if (hit.Region is MenuRegion.TotalFooter or MenuRegion.GroupFooter && hit.ColumnKey is { } column)
        {
            var spec = GetColumn(column);
            var kind = spec?.Kind ?? GridValueKind.Null;
            var scope = hit.Region == MenuRegion.TotalFooter ? SummaryScope.Total : SummaryScope.Group;
            return Enum.GetValues<SummaryOperation>().Select<SummaryOperation, MenuEntry>(operation =>
                new MenuCommand($"summary:{operation}", operation.ToString(), operation.ToString(), IsEnabled: SummaryRules.Allows(kind, operation),
                    IsChecked: Query.Summaries.Contains(new SummaryDescriptor(column, operation, scope))))
                .Prepend(new MenuSection($"{(scope == SummaryScope.Group ? "Group summary" : "Summary")} · {spec?.Header ?? column}"))
                .Append(new MenuSeparator()).Append(new MenuCommand("summary-none", "None", "SummaryNone"))
                .Append(new MenuSeparator()).Append(new MenuCommand("summary-customize", "Customize…", "Function")).ToImmutableArray();
        }

        return hit.Region switch
        {
            MenuRegion.ColumnHeader =>
            [
                new MenuCommand("sort-ascending", "Sort ascending", "ArrowUp"),
                new MenuCommand("sort-descending", "Sort descending", "ArrowDown"),
                new MenuCommand("sort-clear", "Clear sorting", "SortClear", IsEnabled: hit.ColumnKey is { } sortKey && SortIndex(sortKey) >= 0),
                new MenuSeparator(),
                new MenuCommand("group", "Group by this column", "Group"),
                new MenuCommand("group-panel", "Show group panel", "Group", IsChecked: ShowGroupPanel),
                new MenuCommand("columns", "Column chooser", "Columns"),
                new MenuCommand("best-fit", "Best fit", "BestFit"),
                new MenuCommand("best-fit-all", "Best fit (all columns)", "BestFit"),
                new MenuSeparator(),
                new MenuCommand("filter-editor", "Filter editor…", "Filter"),
                new MenuCommand("auto-filter", "Show auto-filter row", "Filter", IsChecked: ShowAutoFilter),
                new MenuCommand("search", "Show search bar", "Search", "Ctrl+F", IsChecked: IsSearchPanelVisible),
                new MenuSeparator(),
                new MenuCommand("hide-column", "Hide this column", "Hide"),
                new MenuCommand("freeze-column", "Freeze column", "Columns", IsChecked: hit.ColumnKey is { } freezeKey && GetColumn(freezeKey)?.IsFrozen == true)
            ],
            MenuRegion.GroupPanel =>
            [
                new MenuCommand("expand-all", "Full expand", "ExpandAll", IsEnabled: Query.Groups.Length > 0),
                new MenuCommand("collapse-all", "Full collapse", "CollapseAll", IsEnabled: Query.Groups.Length > 0),
                new MenuCommand("clear-grouping", "Clear grouping", "Ungroup", IsEnabled: Query.Groups.Length > 0),
                new MenuCommand("group-panel", "Hide group panel", "Hide"),
                .. GroupColumnActions(hit.ColumnKey)
            ],
            MenuRegion.GroupRow =>
            [
                new MenuCommand("toggle-group",
                    VisibleRows.FirstOrDefault(row => row.Key.Value == hit.RowKey)?.Group?.IsExpanded == true ? "Collapse group" : "Expand group",
                    "ChevronDown", IsEnabled: hit.RowKey is not null),
                new MenuCommand("expand-all", "Expand all", "ExpandAll"),
                new MenuCommand("collapse-all", "Collapse all", "CollapseAll"),
                new MenuCommand("ungroup", $"Ungroup '{(hit.ColumnKey is { } groupKey ? GetColumn(groupKey)?.Header ?? groupKey : "")}'",
                    "Ungroup", IsEnabled: hit.ColumnKey is not null)
            ],
            MenuRegion.FilterPanel =>
            [
                new MenuCommand("filter-editor", "Edit filter…", "Edit"),
                new MenuCommand("filter-enable", "Filter enabled", "Filter", IsChecked: Query.FilterEnabled),
                new MenuCommand("filter-clear", "Clear filter", "FilterClear"),
                .. RecentFilterMenu()
            ],
            _ =>
            [
                new MenuCommand("copy-cell", "Copy cell value", "Copy", IsEnabled: hit.RowKey is not null && hit.ColumnKey is not null),
                new MenuCommand("copy", "Copy row", "Copy", "Ctrl+C", IsEnabled: hit.RowKey is not null),
                new MenuCommand("copy-headers", "Copy row with headers", "Copy", "Ctrl+Shift+C", IsEnabled: hit.RowKey is not null),
                new MenuCommand("edit", "Edit cell", "Edit", "F2",
                    IsEnabled: hit.RowKey is not null && hit.ColumnKey is { } editKey && GetColumn(editKey) is { IsReadOnly: false }),
                new MenuSeparator(),
                ExportMenu(),
                new MenuCommand("print", "Print preview…", "Print", "Ctrl+P", IsEnabled: CanExport()),
                .. (RowDetailsTemplate is null ? ImmutableArray<MenuEntry>.Empty
                    : [new MenuCommand("details", "Toggle row details", "TableRows", IsEnabled: hit.RowKey is not null)])
            ]
        };
    }

    private IEnumerable<MenuEntry> RecentFilterMenu()
    {
        var items = _recentFilters.Select((filter, index) => (MenuEntry)new MenuCommand(
            $"recent-filter:{index}", FilterTree.Describe(filter), "Filter",
            IsChecked: Equals(Query.Filter, filter) && Query.FilterEnabled)).ToImmutableArray();
        if (!items.IsDefaultOrEmpty)
            yield return new MenuSubmenu("Recent filters", items, "History");
    }

    private IEnumerable<MenuEntry> GroupColumnActions(string? key)
    {
        if (key is null)
            yield break;
        int index = GroupIndex(key);
        yield return new MenuSeparator();
        yield return new MenuCommand("group-left", "Move group left", "ChevronLeft", IsEnabled: index > 0);
        yield return new MenuCommand("group-right", "Move group right", "ChevronRight", IsEnabled: index >= 0 && index < Query.Groups.Length - 1);
        yield return new MenuCommand("ungroup", "Ungroup", "Ungroup");
    }

    private void ExecuteGridMenu(string id, MenuHit hit)
    {
        var column = hit.ColumnKey;
        if (id.StartsWith("summary:", StringComparison.Ordinal) && column is not null
            && Enum.TryParse<SummaryOperation>(id.AsSpan(8), out var operation))
        {
            ToggleSummary(column, hit.Region == MenuRegion.TotalFooter ? SummaryScope.Total : SummaryScope.Group, operation);
            return;
        }

        if (id.StartsWith("export:", StringComparison.Ordinal))
        {
            _ = ExportViewAsync(id[7..]);
            return;
        }
        if (id.StartsWith("recent-filter:", StringComparison.Ordinal) && int.TryParse(id.AsSpan(14), out int recent)
            && (uint)recent < (uint)_recentFilters.Count)
        {
            SetCurrentValue(QueryProperty, Query with { Filter = _recentFilters[recent], FilterEnabled = true });
            return;
        }

        switch (id)
        {
            case "sort-ascending" when column is not null: SetSort(column, false); break;
            case "sort-descending" when column is not null: SetSort(column, true); break;
            case "sort-clear" when column is not null:
                SetCurrentValue(QueryProperty, Query with { Sort = Query.Sort.Where(item => item.ColumnKey != column).ToImmutableArray() });
                break;
            case "group" when column is not null: GroupBy(column); break;
            case "ungroup" when column is not null: Ungroup(column); break;
            case "clear-grouping": SetCurrentValue(QueryProperty, Query with { Groups = [] }); break;
            case "group-left" when column is not null: GroupBy(column, GroupIndex(column) - 1); break;
            case "group-right" when column is not null: GroupBy(column, GroupIndex(column) + 1); break;
            case "expand-all": ExpandGroups(true); break;
            case "collapse-all": ExpandGroups(false); break;
            case "toggle-group":
                if (VisibleRows.FirstOrDefault(row => row.Key.Value == hit.RowKey)?.Group is { } group)
                    ToggleGroup(group);
                break;
            case "group-panel": SetCurrentValue(ShowGroupPanelProperty, !ShowGroupPanel); break;
            case "auto-filter": SetCurrentValue(ShowAutoFilterProperty, !ShowAutoFilter); break;
            case "search": ShowSearch(); break;
            case "copy-cell": CopyGridCell(hit); break;
            case "copy":
            case "copy-headers": CopyGridRow(hit, id == "copy-headers"); break;
            case "edit":
                if (hit.RowKey is not null && column is not null)
                    SelectCell(new CellAddress(new RowKey(hit.RowKey), column));
                BeginEdit();
                break;
            case "hide-column" when column is not null: SetColumnVisible(column, false); break;
            case "details":
                if (hit.RowKey is { } rowKey) ToggleRowDetails(new RowKey(rowKey));
                break;
            case "freeze-column" when column is not null: SetColumnFrozen(column, GetColumn(column)?.IsFrozen != true); break;
            case "best-fit" when column is not null: BestFitColumn(column); break;
            case "best-fit-all": BestFitAllColumns(); break;
            case "columns": OpenColumnChooser(); break;
            case "filter-values" when column is not null: OpenColumnFilter(column); break;
            case "filter-editor": OpenFilterEditor(); break;
            case "filter-enable": SetCurrentValue(QueryProperty, Query with { FilterEnabled = !Query.FilterEnabled }); break;
            case "filter-clear": SetCurrentValue(QueryProperty, Query with { Filter = null }); break;
            case "summary-none" when column is not null:
                ClearSummaries(column, hit.Region == MenuRegion.TotalFooter ? SummaryScope.Total : SummaryScope.Group);
                break;
            case "summary-customize":
                OpenSummaryEditor(hit.Region == MenuRegion.GroupFooter);
                break;
            case "export":
                if (ExportRequested is { } export) export(this, EventArgs.Empty);
                else _ = ExportViewAsync();
                break;
            case "export-selected": _exportSelectedRowsOnly = !_exportSelectedRowsOnly; break;
            case "print":
                if (PrintPreviewRequested is { } print) print(this, EventArgs.Empty);
                else ShowPrintPreview();
                break;
        }
    }

    private void CopyGridRow(MenuHit hit, bool includeHeaders)
    {
        if (VisibleRows.FirstOrDefault(row => row.Key.Value == hit.RowKey && row.Group is null) is not { } row)
            return;
        var text = string.Join("\t", _displayColumns.Select(column => Display(row, column)));
        if (includeHeaders)
            text = string.Join("\t", _displayColumns.Select(column => column.Header)) + Environment.NewLine + text;
        SetClipboardText(text);
    }

    private void CopyGridCell(MenuHit hit)
    {
        if (hit.ColumnKey is not { } key
            || VisibleRows.FirstOrDefault(row => row.Key.Value == hit.RowKey && row.Group is null) is not { } row
            || _displayColumns.FirstOrDefault(column => column.Key == key) is not { } column)
            return;
        SetClipboardText(Display(row, column));
    }

    private void SetClipboardText(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException ex) { ShowMessage(ex.Message); }
    }

    private void SetSort(string column, bool descending) =>
        SetCurrentValue(QueryProperty, Query with { Sort = [new SortDescriptor(column, descending)] });

    private void SetColumnVisible(string key, bool visible)
    {
        EnsureExplicitColumns();
        int index = IndexOfColumn(key);
        if (index >= 0)
            Columns[index] = Columns[index] with { IsVisible = visible };
    }

    private static void OperationVisibilityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var grid = (MatrixGrid)sender;
        grid.RebuildGroupBar();
        grid.RebuildFilterSurfaces();
    }

    private sealed class GridMenuCommand(MatrixGrid grid, string id) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => parameter is MenuHit;
        public void Execute(object? parameter)
        {
            if (parameter is MenuHit hit)
                grid.ExecuteGridMenu(id, hit);
        }
    }
}
