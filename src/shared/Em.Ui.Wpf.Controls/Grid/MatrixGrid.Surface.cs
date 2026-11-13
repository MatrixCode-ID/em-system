using System.Collections.Immutable;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Em.Controls.Grid;
using Em.Controls.Grid.Data;
using Em.Controls.Grid.Query;
using Em.Ui.Wpf.Controls;
using WpfGrid = System.Windows.Controls.Grid;

namespace Em.Ui.Wpf.Controls;

public sealed partial class MatrixGrid
{
    private ResourceDictionary? _defaultPalette;
    private WpfGrid? _surfaceHost;
    private const double HeaderInset = 12;
    private const double HeaderGap = 6;
    private const double FilterIconWidth = 20;
    private const double SortIconWidth = 20;

    private FrameworkElement BuildSurface()
    {
        // Column-aligned canvases have an explicit width; left alignment keeps them at the rows' origin when the
        // header area is wider than that width (WPF would otherwise center them and shift every header).
        _header = new Canvas { Height = HeaderHeight, HorizontalAlignment = HorizontalAlignment.Left };
        _headerScroll = new ScrollViewer
        {
            Content = HeaderStack(),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalAlignment = HorizontalAlignment.Left,
            Focusable = false
        };
        _headerScroll.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainer");
        _searchBar = BuildSearchBar();
        var groupBar = BuildGroupBar();
        var totalFooter = BuildTotalFooter();
        BuildFilterSurfaces();

        _list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Focusable = false,
            IsTabStop = false,
            ItemContainerStyle = CreateRowContainerStyle(),
            ItemTemplate = CreateRowTemplate()
        };
        _list.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLowest");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
        ScrollViewer.SetCanContentScroll(_list, true);
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
        VirtualizingStackPanel.AddCleanUpVirtualizedItemHandler(_list, (_, e) =>
        {
            if (EditingCell is { } cell && e.Value is GridRow row && row.Key == cell.Row)
                e.Cancel = true;
        });
        AutomationProperties.SetAutomationId(_list, "GridRows");
        _list.Loaded += (_, _) => ConnectScrollViewer();
        _list.SizeChanged += (_, e) =>
        {
            if (Provider is not null && IsLoaded && e.HeightChanged
                && (int)(e.PreviousSize.Height / RowHeight) != VirtualWindowSize)
                RequestFinderRefresh();
        };
        _list.PreviewMouseWheel += (_, e) =>
        {
            if (Provider is not null)
            {
                _ = GoToRowAsync(Math.Max(0, _requestedOffset - Math.Sign(e.Delta) * 3));
                e.Handled = true;
            }
        };
        BuildVirtualScrollbar();
        var body = new DockPanel();
        DockPanel.SetDock(_virtualScroll!, Dock.Right);
        body.Children.Add(_virtualScroll!);
        body.Children.Add(_list);

        _message = new TextBlock
        {
            Margin = new Thickness(12, 6, 12, 6),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        _message.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurfaceVariant");

        var surface = new DockPanel { ClipToBounds = true };
        DockPanel.SetDock(_searchBar, Dock.Top);
        DockPanel.SetDock(groupBar, Dock.Top);
        DockPanel.SetDock(_headerScroll, Dock.Top);
        DockPanel.SetDock(_message, Dock.Bottom);
        surface.Children.Add(groupBar);
        surface.Children.Add(_headerScroll);
        surface.Children.Add(_searchBar);
        AddFilterSurfaces(surface);
        DockPanel.SetDock(totalFooter, Dock.Bottom);
        surface.Children.Add(totalFooter);
        surface.Children.Add(_message);
        surface.Children.Add(body);
        AttachGridMenu(this, FocusedMenuHit);
        _surfaceHost = new WpfGrid();
        _surfaceHost.Children.Add(surface);
        _waiter = BuildWaiter();
        _surfaceHost.Children.Add(_waiter);
        return _surfaceHost;
    }

    private static Style CreateRowContainerStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(FocusableProperty, false));
        style.Setters.Add(new Setter(IsTabStopProperty, false));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        var template = new ControlTemplate(typeof(ListBoxItem))
        {
            VisualTree = new FrameworkElementFactory(typeof(ContentPresenter))
        };
        template.VisualTree.SetValue(ContentPresenter.ContentSourceProperty, "Content");
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }

    private DataTemplate CreateRowTemplate()
    {
        var row = new FrameworkElementFactory(typeof(GridRowView));
        row.SetBinding(GridRowView.OwnerProperty, new Binding
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(MatrixGrid), 1)
        });
        return new DataTemplate { VisualTree = row };
    }

    private void ConnectScrollViewer()
    {
        if (_list is null)
            return;
        if (_bodyScroll is not null)
            _bodyScroll.ScrollChanged -= OnBodyScrolled;
        _bodyScroll = FindVisualChild<ScrollViewer>(_list);
        if (_bodyScroll is not null)
            _bodyScroll.ScrollChanged += OnBodyScrolled;
    }

    private void OnBodyScrolled(object sender, ScrollChangedEventArgs args)
    {
        if (_headerScroll is null || _bodyScroll is null)
            return;
        double viewport = _bodyScroll.ViewportWidth;
        if (viewport > 0 && double.IsFinite(viewport))
            _headerScroll.Width = viewport;
        _headerScroll.HorizontalAlignment = HorizontalAlignment.Left;
        _headerScroll.ScrollToHorizontalOffset(_bodyScroll.HorizontalOffset);
        if (args.HorizontalChange != 0 || args.ViewportWidthChange != 0)
            InvalidateViewport();
        RenderTotalFooter();
        ScrollFilterRow();
    }

    private void RebuildColumns(bool refreshFinderFilter = true)
    {
        IEnumerable<GridColumn> candidates = Columns.Count > 0 ? Columns :
            AutoGenerateColumns ? _generatedColumns : Array.Empty<GridColumn>();
        _displayColumns = candidates.Where(column => column.IsVisible && !string.IsNullOrWhiteSpace(column.Key) && GroupIndex(column.Key) < 0)
            .DistinctBy(column => column.Key).OrderByDescending(column => column.IsFrozen).ToArray();
        if (_displayColumns.Count == 0 && Query.Groups.Length > 0)
            _displayColumns = candidates.Where(column => column.IsVisible).DistinctBy(column => column.Key).Take(1).ToArray();
        RecalculateColumnOffsets();
        RebuildHeader();
        RebuildGroupBar();
        RenderTotalFooter();
        RebuildFilterSurfaces();
        foreach (var row in _realized.ToArray())
            row.Synchronize();
        UpdateFinderColumns(refreshFinderFilter);
    }

    private void RebuildHeader()
    {
        if (_header is null || _rebuildingHeader)
            return;
        _rebuildingHeader = true;
        try
        {
            PopulateVisibleHeaders(replace: true);
        }
        finally
        {
            _rebuildingHeader = false;
        }
    }

    private int SortIndex(string key)
    {
        for (int i = 0; i < Query.Sort.Length; i++)
            if (Query.Sort[i].ColumnKey == key)
                return i;
        return -1;
    }

    private static Thickness HeaderPadding(GridColumn column, bool showFilter)
    {
        double filterInset = HeaderInset + (showFilter ? FilterIconWidth + HeaderGap : 0);
        return column.EffectiveAlignment == GridColumnAlignment.Right
            ? new Thickness(filterInset, 0, HeaderInset, 0)
            : new Thickness(HeaderInset, 0, filterInset, 0);
    }

    private string HeaderSortRank(int sortIndex) => sortIndex >= 0 && Query.Sort.Length > 1
        ? (sortIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : string.Empty;

    private DockPanel CreateHeaderCaption(GridColumn column, int sortIndex)
    {
        bool numeric = column.EffectiveAlignment == GridColumnAlignment.Right;
        bool centered = column.EffectiveAlignment == GridColumnAlignment.Center;
        var label = new TextBlock
        {
            Text = column.Header,
            FontFamily = FontFamily,
            FontSize = FontSize,
            FontWeight = FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignmentFor(column)
        };
        Material.Paint(label, TextBlock.ForegroundProperty, sortIndex < 0 ? "OnSurfaceVariant" : "OnSurface");
        var sort = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = numeric ? new Thickness(0, 0, HeaderGap, 0) : new Thickness(HeaderGap, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        sort.Children.Add(Material.Icon(sortIndex < 0 ? "SortNone" : Query.Sort[sortIndex].Descending ? "SortDesc" : "SortAsc", SortIconWidth));
        var rank = HeaderSortRank(sortIndex);
        if (rank.Length != 0)
        {
            var index = new TextBlock
            {
                Text = rank,
                FontSize = 11, FontFamily = FontFamily, FontWeight = FontWeights.Medium,
                VerticalAlignment = VerticalAlignment.Center
            };
            Material.Paint(index, TextBlock.ForegroundProperty, "OnSurfaceVariant");
            sort.Children.Add(index);
        }
        var content = new DockPanel { FlowDirection = FlowDirection, UseLayoutRounding = UseLayoutRounding,
            HorizontalAlignment = numeric ? HorizontalAlignment.Right : centered ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        TextOptions.SetTextFormattingMode(content, TextOptions.GetTextFormattingMode(this));
        DockPanel.SetDock(sort, numeric ? Dock.Left : Dock.Right);
        content.Children.Add(sort);
        content.Children.Add(label);
        return content;
    }

    private FrameworkElement CreateHeader(GridColumn column)
    {
        int sortIndex = SortIndex(column.Key);
        var button = new Button
        {
            Style = Material.Style("HeaderButton"),
            Content = CreateHeaderCaption(column, sortIndex),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        AutomationProperties.SetName(button, column.Header);
        AttachGridMenu(button, () => new Em.Controls.Grid.Menus.MenuHit(Em.Controls.Grid.Menus.MenuRegion.ColumnHeader, column.Key));
        button.Click += (_, _) => SortBy(column.Key, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);

        Point? dragOrigin = null;
        button.PreviewMouseLeftButtonDown += (_, e) => dragOrigin = e.GetPosition(button);
        button.PreviewMouseLeftButtonUp += (_, _) => dragOrigin = null;
        button.PreviewMouseMove += (_, e) =>
        {
            if (dragOrigin is not { } start || e.LeftButton != MouseButtonState.Pressed)
                return;
            var offset = e.GetPosition(button) - start;
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            dragOrigin = null;
            button.Opacity = 0.38;
            try { DragDrop.DoDragDrop(button, new DataObject(ColumnDragFormat, column.Key), DragDropEffects.Move); }
            finally { button.Opacity = 1; }
        };
        button.AllowDrop = true;
        button.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(ColumnDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };
        button.Drop += (_, e) =>
        {
            if (e.Data.GetData(GroupDragFormat) is string group)
            {
                Ungroup(group);
                e.Handled = true;
                return;
            }
            if (e.Data.GetData(ColumnDragFormat) is string key)
            {
                var after = e.GetPosition(button).X > button.ActualWidth / 2;
                if (e.Data.GetDataPresent(ColumnChooserDragFormat))
                    SetColumnVisible(key, true);
                e.Handled = MoveColumn(key, column.Key, after);
            }
        };

        var gripVisual = new FrameworkElementFactory(typeof(Border));
        gripVisual.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var grip = new Thumb
        {
            Width = 6, Cursor = Cursors.SizeWE, HorizontalAlignment = HorizontalAlignment.Right, Focusable = false,
            Background = Brushes.Transparent,
            Template = new ControlTemplate(typeof(Thumb))
            {
                VisualTree = gripVisual
            }
        };
        double resizedWidth = WidthFor(column);
        grip.DragDelta += (_, e) =>
        {
            resizedWidth = Math.Clamp(resizedWidth + e.HorizontalChange, 44, 2000);
            if (grip.Parent is WpfGrid parent)
                parent.Width = resizedWidth;
        };
        grip.MouseDoubleClick += (_, e) =>
        {
            e.Handled = true;
            grip.CancelDrag();
            BestFitColumn(column.Key);
        };
        grip.DragCompleted += (_, e) =>
        {
            if (!e.Canceled && resizedWidth != WidthFor(column))
                ResizeColumn(column.Key, resizedWidth);
            else if (grip.Parent is WpfGrid parent)
                parent.Width = WidthFor(column);
        };

        var header = new WpfGrid { Width = WidthFor(column), Height = HeaderHeight };
        header.Children.Add(button);
        bool active = FilterTree.ForColumn(Query.Filter, column.Key) is not null;
        var filter = Material.IconButton(active ? "FilterActive" : "Filter", $"Filter {column.Header}", FilterIconWidth);
        bool numeric = column.EffectiveAlignment == GridColumnAlignment.Right;
        filter.HorizontalAlignment = numeric ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        filter.Margin = numeric ? new Thickness(HeaderInset, 0, 0, 0) : new Thickness(0, 0, HeaderInset, 0);
        filter.Click += (_, _) => OpenColumnFilter(column.Key, filter);
        header.MouseEnter += (_, _) => UpdateFilter();
        header.MouseLeave += (_, _) => UpdateFilter();
        header.IsKeyboardFocusWithinChanged += (_, _) => UpdateFilter();
        UpdateFilter();
        header.Children.Add(filter);
        header.Children.Add(grip);
        var frame = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) };
        frame.SetResourceReference(Border.BorderBrushProperty, "MatrixGrid.Brush.OutlineVariant");
        frame.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainer");
        return frame;

        void UpdateFilter()
        {
            bool visible = active || header.IsMouseOver || header.IsKeyboardFocusWithin;
            filter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            button.Padding = HeaderPadding(column, visible);
        }
    }

    /// <summary>Sets the width of a column.</summary>
    /// <param name="key">The column key.</param>
    /// <param name="width">The new width in device-independent pixels.</param>
    /// <returns>True when the column exists.</returns>
    public bool ResizeColumn(string key, double width)
    {
        if (!double.IsFinite(width))
            return false;
        EnsureExplicitColumns();
        int index = IndexOfColumn(key);
        if (index < 0)
            return false;
        var column = Columns[index];
        var clamped = Math.Clamp(width, 44, 2000);
        if (column.Width != clamped)
            Columns[index] = column with { Width = clamped };
        return true;
    }

    internal Brush GetBrush(string role)
    {
        var key = $"MatrixGrid.Brush.{role}";
        if (TryFindResource(key) is Brush brush)
            return brush;
        _defaultPalette ??= new ResourceDictionary
        {
            Source = new Uri("/Em.Ui.Wpf.Controls;component/Themes/Light.xaml", UriKind.Relative)
        };
        return _defaultPalette[key] as Brush ?? Brushes.Transparent;
    }

    private static T? FindVisualChild<T>(DependencyObject source) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is T match)
                return match;
            if (FindVisualChild<T>(child) is { } descendant)
                return descendant;
        }
        return null;
    }
}
