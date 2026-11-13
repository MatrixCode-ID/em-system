using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Em.Documents.Formats;
using Em.Documents.Layout;
using Em.Documents.Model;
using Em.Controls.Grid.Export;
using Em.Controls.Grid.Menus;
using Em.Ui.Wpf.Controls;
using LayoutPage = Em.Documents.Layout.DocumentPage;
using WpfAlignment = System.Windows.HorizontalAlignment;

namespace Em.Ui.Wpf.Controls;

/// <summary>A print preview window with page navigation, zoom, page setup, printing and export.</summary>
public sealed class PrintPreviewWindow : Window
{
    private readonly Func<PageSetup, bool, IProgress<ExportProgress>?, Document> _createDocument;
    private readonly PageSetup _originalSetup;
    private readonly bool _originalFit;
    private readonly DocumentFormatRegistry _formats;
    private readonly PagePresenter _page = new();
    private readonly ScrollViewer _canvas;
    private readonly ObservableCollection<int> _thumbnailPages = [];
    private readonly ItemsControl _thumbnailList = new();
    private readonly HashSet<PageThumbnail> _liveThumbnails = [];
    private readonly Dictionary<int, LayoutPage> _thumbnailCache = new();
    private readonly SemaphoreSlim _thumbnailLoads = new(2);
    private readonly TextBlock _documentInfo = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _paperInfo = new() { FontSize = 12, HorizontalAlignment = WpfAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBox _pageNumber = new() { Width = 64, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pageCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 8, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox _zoom = new() { Width = 150, Height = 36 };
    private readonly ComboBox _paper = new() { ItemsSource = new[] { "A4", "Letter", "Original" } };
    private readonly ListBox _orientation = new() { Style = Material.Style("Segments"), ItemsSource = new[] { "Portrait", "Landscape" } };
    private readonly ComboBox _marginPreset = new() { ItemsSource = new[] { "Normal (12.7 mm)", "Narrow (6.35 mm)", "Wide (25.4 mm)", "Custom" } };
    private readonly TextBox[] _margins = [new(), new(), new(), new()];
    private readonly TextBox _copies = new() { Text = "1" };
    private readonly TextBox _range = new() { ToolTip = "Enter a range such as 2-5.", IsEnabled = false };
    private readonly ListBox _pageRangeMode = new() { Style = Material.Style("Segments"), ItemsSource = new[] { "All", "Current", "Range" }, SelectedIndex = 0 };
    private readonly CheckBox _fit = new() { Content = "Fit to page width" };
    private readonly CheckBox _repeatHeaders = new() { Content = "Repeat column headers", IsChecked = true };
    private readonly CheckBox _showHeading = new() { Content = "Header: title and print date", IsChecked = true };
    private readonly CheckBox _pageNumbers = new() { Content = "Footer: Page N of M", IsChecked = true };
    private readonly CheckBox _showSummaries = new() { Content = "Group footers and totals", IsChecked = true };
    private readonly Button _cancelExport = new() { Content = "Cancel export", Visibility = Visibility.Collapsed };
    private readonly Button _previous = Material.IconButton("ChevronLeft", "Previous page");
    private readonly Button _next = Material.IconButton("ChevronRight", "Next page");
    private readonly Dictionary<int, LayoutPage> _cache = new();
    private readonly Paginator _paginator = new();
    private PageIndex _index = new();
    private CancellationTokenSource _documentLifetime = new();

    private CancellationTokenSource? _navigation;
    private CancellationTokenSource? _counting;
    private CancellationTokenSource? _export;
    private int _countedPages;
    private Document? _document;
    private PageSetup _setup;
    private bool _appliedFit;
    private bool _useCurrentSetup = true;
    private PageContentOptions? _appliedContent;
    private string? _exportTitle;

    /// <summary>Creates a preview.</summary>
    /// <param name="createDocument">Builds the document for a page setup and fit option, reporting row progress.</param>
    /// <param name="formats">The export writers offered by the window.</param>
    /// <param name="setup">The initial page setup, or null for A4 portrait.</param>
    /// <param name="fit">Whether columns start scaled to the page width.</param>
    /// <param name="dark">Whether the window uses the dark palette.</param>
    public PrintPreviewWindow(Func<PageSetup, bool, IProgress<ExportProgress>?, Document> createDocument,
        DocumentFormatRegistry formats, PageSetup? setup = null, bool fit = true, bool dark = false)
    {
        _createDocument = createDocument ?? throw new ArgumentNullException(nameof(createDocument));
        _formats = formats ?? throw new ArgumentNullException(nameof(formats));
        _setup = _originalSetup = setup ?? new PageSetup();
        _appliedFit = _originalFit = fit;
        Title = "Print preview";
        Width = Math.Min(1848, SystemParameters.WorkArea.Width);
        Height = Math.Min(1024, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(780, Width);
        MinHeight = Math.Min(530, Height);
        ResizeMode = ResizeMode.NoResize;
        Material.ConfigureDialog(this, null);
        GridTheme.SetMode(this, dark ? GridThemeMode.Dark : GridThemeMode.Light);
        Material.Paint(_documentInfo, TextBlock.ForegroundProperty, "OnSurfaceVariant");
        Material.Paint(_paperInfo, TextBlock.ForegroundProperty, "OnSurfaceVariant");
        var paper = new Border
        {
            Child = _page, HorizontalAlignment = WpfAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.23 }
        };
        var canvasContent = new StackPanel { Margin = new Thickness(20), HorizontalAlignment = WpfAlignment.Center };
        canvasContent.Children.Add(paper);
        canvasContent.Children.Add(_paperInfo);
        _canvas = new ScrollViewer
        {
            Content = canvasContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        _canvas.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerHighest");
        _canvas.SizeChanged += (_, _) => UpdateZoom();
        Material.SetDialogContent(this, BuildSurface(), "SurfaceContainerLow", new Thickness(0));
        Loaded += (_, _) => { if (_document is null) _ = RefreshAsync(_setup, fit); };
        Closed += (_, _) => { _navigation?.Cancel(); _counting?.Cancel(); _documentLifetime.Cancel(); _export?.Cancel(); };
    }

    /// <summary>Gets the one-based number of the page shown.</summary>
    public int CurrentPage { get; private set; }
    /// <summary>Gets the number of pages counted so far.</summary>
    public int PageCount { get; private set; }
    /// <summary>Gets the laid-out page shown, or null before the first page is ready.</summary>
    public LayoutPage? CurrentPageModel => _page.Page;
    /// <summary>Gets the export formats offered by the window.</summary>
    public IReadOnlyList<DocumentFormat> ExportFormats => _formats.Formats;

    /// <summary>
    /// Rebuilds the document, shows its first page as soon as it is laid out, and then counts the remaining pages
    /// in the background. The task completes when the page count is known.
    /// </summary>
    public async Task RefreshAsync(PageSetup setup, bool fit)
    {
        setup.Validate();
        _navigation?.Cancel();
        _counting?.Cancel();
        _counting = null;
        Document document;
        try
        {
            _setup = setup;
            _appliedFit = fit;
            document = _document = CreateDocument(setup, fit, _appliedContent);
            if (_appliedContent is null)
            {
                _showHeading.IsChecked = document.Sections.All(section => section.ShowHeading);
                _pageNumbers.IsChecked = document.Sections.All(section => section.PageNumbers);
                _repeatHeaders.IsChecked = document.Sections.SelectMany(section => section.Blocks)
                    .OfType<DocumentTable>().All(table => table.RepeatHeader);
            }
            _cache.Clear();
            _thumbnailCache.Clear();
            _thumbnailPages.Clear();
            _documentLifetime.Cancel();
            _documentLifetime = new CancellationTokenSource();
            _index = new PageIndex();
            PageCount = CurrentPage = _countedPages = 0;
            _page.Page = null;
            UpdateNavigation();
        }
        catch (Exception ex) { _status.Text = ex.GetBaseException().Message; return; }

        // Large grids have many thousands of pages; counting them all first would leave the preview blank.
        await ShowPageAsync(1);
        if (ReferenceEquals(_document, document))
            await CountPagesAsync(document);
    }

    private async Task CountPagesAsync(Document document)
    {
        using var request = _counting = new CancellationTokenSource();
        var progress = new Progress<int>(count =>
        {
            if (ReferenceEquals(_counting, request) && PageCount == 0)
            {
                _countedPages = Math.Max(_countedPages, count);
                UpdatePageCountText();
            }
        });
        try
        {
            var index = _index;
            int total = await Task.Run(() => _paginator.CountPagesAsync(document, progress, request.Token, index), request.Token);
            if (!ReferenceEquals(_counting, request) || !ReferenceEquals(_document, document))
                return;
            PageCount = _countedPages = total;
            // Pages laid out while counting have no "of N" footer; lay out the visible pages again with the total.
            _cache.Clear();
            _thumbnailCache.Clear();
            foreach (var thumbnail in _liveThumbnails.ToArray())
                thumbnail.Refresh();
            if (_navigation is null)
                await ShowPageAsync(Math.Max(1, CurrentPage));
            else
                UpdateNavigation();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { _status.Text = ex.GetBaseException().Message; }
        finally
        {
            if (ReferenceEquals(_counting, request))
                _counting = null;
        }
    }

    private int AvailablePages => PageCount > 0 ? PageCount
        : Math.Max(1, Math.Max(_countedPages, _cache.Count == 0 ? 0 : _cache.Keys.Max()));

    /// <summary>Shows a page, laying it out when needed.</summary>
    /// <param name="pageNumber">The one-based page number.</param>
    /// <returns>A task that completes when the page is shown.</returns>
    public async Task ShowPageAsync(int pageNumber)
    {
        if (_document is not { } document)
            return;
        int number = Math.Clamp(pageNumber, 1, AvailablePages);
        var request = BeginNavigation();
        try
        {
            if (!_cache.ContainsKey(number))
            {
                _status.Text = $"Loading page {number:N0}…";
                int total = PageCount;
                var pages = await Task.Run(() => LoadPagesAsync(document, Math.Max(1, number - 2), number + 2, total,
                    request.Token), request.Token);
                if (!ReferenceEquals(request, _navigation) || request.IsCancellationRequested)
                    return;
                foreach (var page in pages)
                    _cache[page.Number] = page;
                if (_cache.Count > 16)
                    foreach (int distant in _cache.Keys.OrderByDescending(key => Math.Abs(key - number)).Take(_cache.Count - 16).ToArray())
                        _cache.Remove(distant);
            }

            if (!_cache.TryGetValue(number, out var selected))
            {
                _status.Text = string.Empty;
                return;
            }
            CurrentPage = number;
            _page.Page = selected;
            _status.Text = string.Empty;
            _paperInfo.Text = $"{_setup.PageWidth * 25.4 / 72:0.#} × {_setup.PageHeight * 25.4 / 72:0.#} mm · {_setup.Orientation}" +
                (_appliedFit ? " · Fit to page width" : string.Empty);
            _canvas.ScrollToHome();
            UpdateZoom();
            UpdateNavigation();
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) { _status.Text = ex.GetBaseException().Message; }
        finally { EndNavigation(request); }
    }

    /// <summary>
    /// Lays out pages <paramref name="first"/> through <paramref name="last"/>, resuming at the nearest recorded page
    /// start instead of paginating every earlier page.
    /// </summary>
    private async Task<List<LayoutPage>> LoadPagesAsync(Document document, int first, int last, int total,
        CancellationToken token)
    {
        var results = new List<LayoutPage>();
        var pages = _index.TryFind(first, out int start, out var checkpoint) && start > 1
            ? _paginator.PagesFromAsync(document, total, start, checkpoint, token)
            : _paginator.PagesAsync(document, total, token);
        await foreach (var page in pages.WithCancellation(token).ConfigureAwait(false))
        {
            if (page.Number >= first)
                results.Add(page);
            if (page.Number >= last)
                break;
        }
        return results;
    }

    private async Task<LayoutPage?> LoadThumbnailAsync(int number, Func<bool> stillWanted)
    {
        if (_thumbnailCache.TryGetValue(number, out var cached))
            return cached;
        if (_document is not { } document)
            return null;
        var token = _documentLifetime.Token;
        int total = PageCount;
        await _thumbnailLoads.WaitAsync(token);
        try
        {
            // Fast scrolling recycles a thumbnail before its turn comes; skip pages that are no longer shown.
            if (!stillWanted() || !ReferenceEquals(document, _document))
                return null;
            if (_thumbnailCache.TryGetValue(number, out cached))
                return cached;
            var pages = await Task.Run(() => LoadPagesAsync(document, number, number, total, token), token);
            if (!ReferenceEquals(document, _document) || total != PageCount)
                return null;
            var page = pages.FirstOrDefault(candidate => candidate.Number == number);
            if (page is not null)
            {
                if (_thumbnailCache.Count >= 96)
                    foreach (int distant in _thumbnailCache.Keys.OrderByDescending(key => Math.Abs(key - number)).Take(32).ToArray())
                        _thumbnailCache.Remove(distant);
                _thumbnailCache[number] = page;
            }
            return page;
        }
        finally { _thumbnailLoads.Release(); }
    }

    private void SyncThumbnailPages()
    {
        int available = _document is null || CurrentPage == 0 && PageCount == 0 && _countedPages == 0 ? 0 : AvailablePages;
        while (_thumbnailPages.Count > available)
            _thumbnailPages.RemoveAt(_thumbnailPages.Count - 1);
        while (_thumbnailPages.Count < available)
            _thumbnailPages.Add(_thumbnailPages.Count + 1);
    }

    private ItemsControl BuildThumbnailList()
    {
        _thumbnailList.ItemsSource = _thumbnailPages;
        _thumbnailList.Width = 176;
        _thumbnailList.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLow");
        _thumbnailList.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(VirtualizingStackPanel)));
        _thumbnailList.ItemTemplate = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(PageThumbnail)) };
        VirtualizingPanel.SetIsVirtualizing(_thumbnailList, true);
        VirtualizingPanel.SetVirtualizationMode(_thumbnailList, VirtualizationMode.Recycling);
        var scroll = new FrameworkElementFactory(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        _thumbnailList.Template = new ControlTemplate(typeof(ItemsControl)) { VisualTree = scroll };
        AutomationProperties.SetName(_thumbnailList, "Pages");
        return _thumbnailList;
    }

    private void BringThumbnailIntoView()
    {
        if (CurrentPage < 1 || _liveThumbnails.Any(thumbnail => thumbnail.Number == CurrentPage))
            return;
        if (FindPanel(_thumbnailList) is { } panel && CurrentPage <= _thumbnailPages.Count)
            panel.BringIndexIntoViewPublic(CurrentPage - 1);

        static VirtualizingStackPanel? FindPanel(DependencyObject parent)
        {
            for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var child = VisualTreeHelper.GetChild(parent, index);
                if (child is VirtualizingStackPanel panel)
                    return panel;
                if (FindPanel(child) is { } nested)
                    return nested;
            }
            return null;
        }
    }

    /// <summary>A page card in the virtualized thumbnail list; it lays out its page on demand.</summary>
    private sealed class PageThumbnail : Button
    {
        private readonly PagePresenter _presenter = new()
        {
            HorizontalAlignment = WpfAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        private readonly Border _frame;
        private readonly TextBlock _label = new() { HorizontalAlignment = WpfAlignment.Center, FontSize = 12 };
        private PrintPreviewWindow? _owner;
        private int _version;

        public PageThumbnail()
        {
            Style = Material.Style("HeaderButton");
            Margin = new Thickness(8, 6, 8, 0);
            Padding = new Thickness(4);
            SetResourceReference(BorderBrushProperty, "MatrixGrid.Brush.Primary");
            _frame = new Border { Child = _presenter, Width = 132, Height = 94, Padding = new Thickness(2),
                CornerRadius = new CornerRadius(4) };
            Material.Paint(_frame, Border.BorderBrushProperty, "Primary");
            var card = new StackPanel { HorizontalAlignment = WpfAlignment.Center };
            card.Children.Add(_frame);
            card.Children.Add(_label);
            Content = card;
            DataContextChanged += (_, _) => Refresh();
            Loaded += (_, _) =>
            {
                _owner ??= Window.GetWindow(this) as PrintPreviewWindow;
                _owner?._liveThumbnails.Add(this);
                Refresh();
            };
            Unloaded += (_, _) => _owner?._liveThumbnails.Remove(this);
            Click += (_, _) =>
            {
                if (_owner is { } owner && Number > 0)
                    _ = owner.ShowPageAsync(Number);
            };
        }

        public int Number => DataContext is int number ? number : 0;

        public void UpdateSelection() =>
            _frame.BorderThickness = new Thickness(_owner is { } owner && owner.CurrentPage == Number ? 2 : 0);

        public async void Refresh()
        {
            int number = Number;
            int version = ++_version;
            _label.Text = number > 0 ? $"Page {number:N0}" : string.Empty;
            AutomationProperties.SetName(this, $"Show page {number}");
            _owner ??= Window.GetWindow(this) as PrintPreviewWindow;
            UpdateSelection();
            if (_owner is not { } owner || number < 1)
            {
                _presenter.Page = null;
                return;
            }
            if (owner._thumbnailCache.TryGetValue(number, out var cached))
            {
                Show(cached);
                return;
            }
            _presenter.Page = null;
            try
            {
                var page = await owner.LoadThumbnailAsync(number, () => version == _version && IsLoaded);
                if (version == _version && page is not null)
                    Show(page);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { _presenter.Page = null; }
        }

        private void Show(LayoutPage page)
        {
            _presenter.Zoom = Math.Min(128 / page.Setup.PageWidth, 90 / page.Setup.PageHeight) / PagePresenter.DipsPerPoint;
            _presenter.Page = page;
        }
    }

    private FrameworkElement BuildSurface()
    {
        var root = new DockPanel();
        var bar = BuildToolbar();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        var thumbnails = BuildThumbnailList();
        DockPanel.SetDock(thumbnails, Dock.Left);
        root.Children.Add(thumbnails);
        var settings = BuildSettings();
        DockPanel.SetDock(settings, Dock.Right);
        root.Children.Add(settings);
        root.Children.Add(_canvas);
        return root;
    }

    private FrameworkElement BuildToolbar()
    {
        var toolbar = new DockPanel { MinHeight = 64, Margin = new Thickness(16, 0, 12, 0) };
        var heading = new StackPanel { Width = 190, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        heading.MouseLeftButtonDown += (_, _) => DragMove();
        heading.Children.Add(new TextBlock { Text = "Print preview", FontSize = 22 });
        heading.Children.Add(_documentInfo);
        DockPanel.SetDock(heading, Dock.Left);
        toolbar.Children.Add(heading);

        var commands = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(commands, Dock.Right);
        toolbar.Children.Add(commands);
        var navigation = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(navigation);
        _previous.Click += (_, _) => _ = ShowPageAsync(CurrentPage - 1);
        _next.Click += (_, _) => _ = ShowPageAsync(CurrentPage + 1);
        navigation.Children.Add(_previous);
        AutomationProperties.SetName(_pageNumber, "Page number");
        _pageNumber.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && int.TryParse(_pageNumber.Text, out int number))
                _ = ShowPageAsync(number);
        };
        navigation.Children.Add(_pageNumber);
        navigation.Children.Add(_pageCount);
        navigation.Children.Add(_next);

        _zoom.ItemsSource = new[] { "Fit page", "Fit width", "50%", "75%", "100%", "125%", "150%", "200%" };
        _zoom.SelectedIndex = 0;
        _zoom.SelectionChanged += (_, _) => UpdateZoom();
        AutomationProperties.SetName(_zoom, "Zoom");
        var minus = Material.IconButton("Minus", "Zoom out");
        minus.Click += (_, _) => ChangeZoom(-1);
        var plus = Material.IconButton("Plus", "Zoom in");
        plus.Click += (_, _) => ChangeZoom(1);
        navigation.Children.Add(minus);
        navigation.Children.Add(_zoom);
        navigation.Children.Add(plus);

        var export = Button("Export", "Export formats", "TonalButton");
        var exportLabel = new StackPanel { Orientation = Orientation.Horizontal };
        exportLabel.Children.Add(new TextBlock { Text = "Export", VerticalAlignment = VerticalAlignment.Center });
        exportLabel.Children.Add(Material.Icon("ChevronDown", 18, "OnSecondaryContainer"));
        export.Content = exportLabel;
        var menu = MenuRenderer.Attach(export, () => new MenuOpeningEventArgs(new MenuHit(MenuRegion.RowCell),
            _formats.Formats.Select<DocumentFormat, MenuEntry>(format =>
                new MenuCommand("format:" + format.Id, $"{format.DisplayName} ({format.Extension})", Material.FileIcon(format.Id)))
                .Prepend(new MenuSection("Export document as"))
                .Append(new MenuSeparator())
                .Append(new MenuCommand("current", "Use current page setup", "PageSetup", IsChecked: _useCurrentSetup))
                .Append(new MenuCommand("options", "Export options…", "Export"))
                .ToImmutableArray()), id => new PreviewCommand(() =>
                {
                    if (id == "current")
                        _useCurrentSetup = !_useCurrentSetup;
                    else if (id == "options")
                        OpenExportOptions();
                    else if (id.StartsWith("format:", StringComparison.Ordinal))
                        _ = ExportAsync(id[7..]);
                }));
        menu.Placement = PlacementMode.Bottom;
        export.Click += (_, _) => menu.IsOpen = true;
        commands.Children.Add(export);
        var print = Button("Print", "Print document", "FilledButton");
        print.Click += (_, _) => Print();
        commands.Children.Add(print);
        _cancelExport.Click += (_, _) => _export?.Cancel();
        commands.Children.Add(_cancelExport);
        var close = Material.IconButton("Close", "Close print preview");
        close.IsCancel = true;
        close.Click += (_, _) => Close();
        commands.Children.Add(close);
        return toolbar;
    }

    private FrameworkElement BuildSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Printer", FontSize = 16, FontWeight = FontWeights.Medium });
        panel.Children.Add(new TextBlock { Text = "Default printer · change when printing", TextWrapping = TextWrapping.Wrap });
        Field("Copies", _copies);
        Field("Pages", _pageRangeMode);
        _range.Margin = new Thickness(0, 4, 0, 0);
        AutomationProperties.SetName(_range, "Page range");
        panel.Children.Add(_range);
        _pageRangeMode.SelectionChanged += (_, _) => _range.IsEnabled = _pageRangeMode.SelectedIndex == 2;
        panel.Children.Add(new TextBlock { Text = "Paper and layout", FontSize = 16, FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 16, 0, 0) });
        Field("Paper size", _paper);
        Field("Orientation", _orientation);
        Field("Margins", _marginPreset);
        var labels = new[] { "Left margin (pt)", "Top margin (pt)", "Right margin (pt)", "Bottom margin (pt)" };
        var values = new[] { _setup.Left, _setup.Top, _setup.Right, _setup.Bottom };
        var customMargins = new StackPanel();
        for (int index = 0; index < labels.Length; index++)
        {
            _margins[index].Text = values[index].ToString(CultureInfo.CurrentCulture);
            Field(labels[index], _margins[index], customMargins);
        }
        panel.Children.Add(customMargins);
        _marginPreset.SelectionChanged += (_, _) => customMargins.Visibility =
            _marginPreset.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        _marginPreset.SelectedIndex = values.All(value => value == 36) ? 0 : values.All(value => value == 18) ? 1
            : values.All(value => value == 72) ? 2 : 3;
        _paper.SelectedIndex = (_setup.Width, _setup.Height) switch { (595.28, 841.89) => 0, (612, 792) => 1, _ => 2 };
        _orientation.SelectedIndex = _setup.Orientation == PageOrientation.Landscape ? 1 : 0;
        _fit.IsChecked = _appliedFit;
        foreach (var option in new[] { _fit, _repeatHeaders, _showHeading, _pageNumbers, _showSummaries })
        {
            option.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(option);
        }
        var apply = Button("Page setup…", "Apply page setup", "OutlinedButton");
        apply.Margin = new Thickness(0, 8, 0, 8);
        apply.Click += async (_, _) =>
        {
            try
            {
                var setup = ReadSetup();
                setup.Validate();
                _appliedContent = new PageContentOptions(_repeatHeaders.IsChecked == true, _showHeading.IsChecked == true,
                    _pageNumbers.IsChecked == true, _showSummaries.IsChecked == true);
                await RefreshAsync(setup, _fit.IsChecked == true);
            }
            catch (Exception ex) { _status.Text = ex.GetBaseException().Message; }
        };
        panel.Children.Add(apply);
        panel.Children.Add(_status);
        var scroller = new ScrollViewer { Content = panel, Width = Width >= 1120 ? 360 : 252,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroller.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLow");
        return scroller;

        void Field(string caption, Control control, StackPanel? target = null)
        {
            target ??= panel;
            target.Children.Add(new TextBlock { Text = caption, Margin = new Thickness(0, 8, 0, 3) });
            AutomationProperties.SetName(control, caption);
            target.Children.Add(control);
        }
    }

    private PageSetup ReadSetup()
    {
        var values = new double[4];
        if (_marginPreset.SelectedIndex == 3)
        {
            for (int index = 0; index < values.Length; index++)
                if (!double.TryParse(_margins[index].Text, NumberStyles.Float, CultureInfo.CurrentCulture, out values[index]))
                    throw new FormatException("Enter each margin as a number in points.");
        }
        else
            Array.Fill(values, _marginPreset.SelectedIndex switch { 1 => 18d, 2 => 72d, _ => 36d });
        var size = _paper.SelectedIndex switch { 0 => (595.28, 841.89), 1 => (612d, 792d), _ => (_originalSetup.Width, _originalSetup.Height) };
        return new PageSetup(size.Item1, size.Item2, values[0], values[1], values[2], values[3],
            _orientation.SelectedIndex == 1 ? PageOrientation.Landscape : PageOrientation.Portrait);
    }

    private void UpdatePageCountText()
    {
        bool counting = PageCount == 0 && _document is not null;
        string pages = counting ? $"{_countedPages:N0}+" : $"{PageCount:N0}";
        _documentInfo.Text = _document is null ? string.Empty
            : counting ? $"{_document.Title} · counting pages… {pages}" : $"{_document.Title} · {pages} pages";
        _pageCount.Text = $"of {pages}";
        _next.IsEnabled = CurrentPage > 0 && CurrentPage < AvailablePages;
        SyncThumbnailPages();
    }

    private void UpdateNavigation()
    {
        _pageNumber.Text = CurrentPage == 0 ? "" : CurrentPage.ToString(CultureInfo.CurrentCulture);
        _previous.IsEnabled = CurrentPage > 1;
        UpdatePageCountText();
        foreach (var thumbnail in _liveThumbnails)
            thumbnail.UpdateSelection();
        BringThumbnailIntoView();
    }

    private void ChangeZoom(int direction)
    {
        if (_zoom.SelectedIndex < 2)
        {
            double current = _page.Zoom;
            var levels = new[] { 0.5, 0.75, 1, 1.25, 1.5, 2 };
            var next = direction > 0 ? Array.FindIndex(levels, level => level > current)
                : Array.FindLastIndex(levels, level => level < current);
            if (next >= 0)
                _zoom.SelectedIndex = next + 2;
            return;
        }
        _zoom.SelectedIndex = Math.Clamp(_zoom.SelectedIndex + direction, 2, _zoom.Items.Count - 1);
    }

    private void UpdateZoom()
    {
        if (_page.Page is not { } page || _zoom.SelectedItem is not string mode)
            return;
        double width = Math.Max(100, _canvas.ViewportWidth - 40) / (page.Setup.PageWidth * PagePresenter.DipsPerPoint);
        double height = Math.Max(100, _canvas.ViewportHeight - 40) / (page.Setup.PageHeight * PagePresenter.DipsPerPoint);
        _page.Zoom = Math.Clamp(mode switch
        {
            "Fit page" => Math.Min(width, height),
            "Fit width" => width,
            _ => double.Parse(mode.TrimEnd('%'), CultureInfo.InvariantCulture) / 100
        }, 0.1, 3);
    }

    private void Print()
    {
        if (_document is not { } document)
            return;
        if (PageCount == 0)
        {
            _status.Text = "Printing is available after the page count is ready.";
            return;
        }
        try
        {
            var range = ReadRange();
            var dialog = new PrintDialog { MinPage = 1, MaxPage = (uint)PageCount, UserPageRangeEnabled = true,
                PageRangeSelection = range == (1, PageCount) ? PageRangeSelection.AllPages : PageRangeSelection.UserPages,
                PageRange = new PageRange(range.Start, range.End) };
            if (dialog.ShowDialog() != true)
                return;
            if (int.TryParse(_copies.Text, out int copies) && copies is > 0 and <= 999)
                dialog.PrintTicket.CopyCount = copies;
            if (dialog.PageRangeSelection == PageRangeSelection.UserPages)
                range = (dialog.PageRange.PageFrom, dialog.PageRange.PageTo);
            var paginator = new WpfDocumentPaginator(document, PageCount, range.Start, range.End,
                new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight), _appliedFit);
            try { dialog.PrintDocument(paginator, document.Title); }
            finally { paginator.Close(); }
            _status.Text = "Sent to printer";
        }
        catch (Exception ex) { _status.Text = ex.GetBaseException().Message; }
    }

    private (int Start, int End) ReadRange()
    {
        if (_pageRangeMode.SelectedIndex == 0)
            return (1, PageCount);
        if (_pageRangeMode.SelectedIndex == 1)
            return (Math.Max(1, CurrentPage), Math.Max(1, CurrentPage));
        var parts = _range.Text.Split('-', 2, StringSplitOptions.TrimEntries);
        if (!int.TryParse(parts[0], out int start))
            throw new FormatException("Enter a page number or range.");
        int end = start;
        if (parts.Length == 2 && !int.TryParse(parts[1], out end))
            throw new FormatException("Enter a page number or range.");
        if (start < 1 || start > end || end > PageCount)
            throw new FormatException($"Use pages 1 through {PageCount}.");
        return (start, end);
    }

    private async Task ExportAsync(string formatId)
    {
        if (_export is not null || _document is null)
            return;
        var file = DocumentFileExport.ChooseFile(this, _formats, _exportTitle ?? _document.Title, formatId);
        if (file is null)
            return;
        using var request = _export = new CancellationTokenSource();
        _cancelExport.Visibility = Visibility.Visible;
        try
        {
            long rowsWritten = 0;
            int pagesWritten = 0;
            void ShowProgress()
            {
                if (ReferenceEquals(_export, request))
                    _status.Text = $"Exporting {rowsWritten:N0} rows" +
                        (pagesWritten > 0 ? $" · {pagesWritten:N0} pages written…" : "…");
            }
            var progress = new Progress<ExportProgress>(update => { rowsWritten = update.RowsWritten; ShowProgress(); });
            var document = CreateDocument(_useCurrentSetup ? _setup : _originalSetup,
                _useCurrentSetup ? _appliedFit : _originalFit, _useCurrentSetup ? _appliedContent : null, progress) with
            {
                PageProgress = new Progress<int>(pages => { pagesWritten = pages; ShowProgress(); })
            };
            if (_exportTitle is { } title)
                document = document with { Title = title };
            await DocumentFileExport.WriteFileAsync(_formats.Get(file.Value.FormatId), document, file.Value.Path, request.Token);
            _status.Text = "Export complete";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { _status.Text = "Export canceled"; }
        catch (Exception ex) { _status.Text = ex.GetBaseException().Message; }
        finally { _cancelExport.Visibility = Visibility.Collapsed; _export = null; }
    }

    private Document CreateDocument(PageSetup setup, bool fit, PageContentOptions? content, IProgress<ExportProgress>? progress = null)
    {
        var document = _createDocument(setup, fit, progress);
        if (content is null)
            return document;
        return document with
        {
            Sections = document.Sections.Select(section => section with
            {
                ShowHeading = content.ShowHeading,
                PageNumbers = content.PageNumbers,
                Blocks = section.Blocks.Select(block => block is DocumentTable table ? table with
                {
                    RepeatHeader = content.RepeatHeaders,
                    OpenRows = content.ShowSummaries ? table.OpenRows : token => DataRows(table, token),
                    // Without summary rows the row positions no longer match the source rows.
                    OpenRowsFrom = content.ShowSummaries ? table.OpenRowsFrom : null
                } : block).ToImmutableArray()
            }).ToImmutableArray()
        };

        static async IAsyncEnumerable<DocumentRow> DataRows(DocumentTable table, [EnumeratorCancellation] CancellationToken token)
        {
            await foreach (var row in table.OpenRows(token).WithCancellation(token).ConfigureAwait(false))
                if (row.Kind != DocumentRowKind.Summary)
                    yield return row;
        }
    }

    private void OpenExportOptions()
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Export title" });
        var title = new TextBox { Text = _exportTitle ?? _document?.Title ?? "Grid", Margin = new Thickness(0, 4, 0, 8) };
        var current = new CheckBox { Content = "Use current page setup", IsChecked = _useCurrentSetup };
        var apply = Button("Apply", "Apply export options", "FilledButton");
        apply.IsDefault = true;
        panel.Children.Add(title);
        panel.Children.Add(current);
        var cancel = Material.Button("Cancel");
        cancel.IsCancel = true;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = WpfAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(cancel);
        actions.Children.Add(apply);
        panel.Children.Add(actions);
        var dialog = Material.Dialog(this, "Export options", panel, 390);
        apply.Click += (_, _) => dialog.DialogResult = true;
        if (dialog.ShowDialog() == true)
        {
            _useCurrentSetup = current.IsChecked == true;
            _exportTitle = string.IsNullOrWhiteSpace(title.Text) ? null : title.Text.Trim();
        }
    }

    private CancellationTokenSource BeginNavigation()
    {
        _navigation?.Cancel();
        return _navigation = new CancellationTokenSource();
    }

    private void EndNavigation(CancellationTokenSource request)
    {
        if (ReferenceEquals(_navigation, request))
            _navigation = null;
        request.Dispose();
    }

    private static Button Button(string label, string accessibleName, string style = "Button")
    {
        var button = Material.Button(label, style);
        button.Margin = new Thickness(4, 0, 4, 0);
        AutomationProperties.SetName(button, accessibleName);
        return button;
    }

    private sealed class PreviewCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    private sealed record PageContentOptions(bool RepeatHeaders, bool ShowHeading, bool PageNumbers, bool ShowSummaries);
}
