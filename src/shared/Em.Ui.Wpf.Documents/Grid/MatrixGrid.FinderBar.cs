using System.Collections.Immutable;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Em.Grid.Menus;
using Em.Ui.Wpf.Documents.Menus;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Grid;

public sealed partial class MatrixGrid
{
    private Button? _finderClear;
    private Button? _finderPrevious;
    private Button? _finderNext;
    private ContextMenu? _searchOptionsMenu;
    private ContextMenu? _gridSettingsMenu;
    private TextBlock? _finderTitle;
    private TextBlock? _finderRowCount;
    private TextBlock? _finderCounter;
    private TextBlock? _finderStatus;
    private TextBlock? _finderScope;
    private Border? _finderFilterIndicator;
    private FrameworkElement? _finderFilterIcon;
    private FrameworkElement? _finderOptionsChevron;
    private Button? _finderOptions;
    private bool _finderCondensed;

    private Border BuildSearchBar()
    {
        var identity = new StackPanel { Width = 72, VerticalAlignment = VerticalAlignment.Center };
        _finderTitle = FinderLabel(16, "OnSurface");
        _finderTitle.FontWeight = FontWeights.Medium;
        _finderRowCount = FinderLabel(12, "OnSurfaceVariant");
        identity.Children.Add(_finderTitle);
        identity.Children.Add(_finderRowCount);

        _search = new TextBox
        {
            Height = 40, Padding = new Thickness(0), Style = Material.Style("BareInput"), FocusVisualStyle = null
        };
        AutomationProperties.SetName(_search, "Find in grid");
        AutomationProperties.SetAutomationId(_search, "GridSearch");
        var placeholder = FinderLabel(14, "OnSurfaceVariant");
        placeholder.Text = "Find in grid";
        placeholder.IsHitTestVisible = false;
        var input = new System.Windows.Controls.Grid { ClipToBounds = true };
        input.Children.Add(_search);
        input.Children.Add(placeholder);
        _search.TextChanged += (_, _) =>
        {
            placeholder.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_finderClear is { } clear)
            {
                if (_search.Text.Length == 0 && clear.IsKeyboardFocused)
                    _search.Focus();
                clear.IsEnabled = _search.Text.Length > 0;
            }
            if (_updatingSearch)
                return;
            ScheduleFinderSearch();
        };

        _finderClear = Material.IconButton("Close", "Clear query", 40);
        _finderClear.Click += (_, _) =>
        {
            SetFinderText(string.Empty);
            ApplySearch();
            _search.Focus();
        };
        _finderPrevious = Material.IconButton("ChevronUp", "Previous match", 40);
        _finderPrevious.Click += (_, _) => NavigateFinder(previous: true);
        _finderNext = Material.IconButton("ChevronDown", "Next match", 40);
        _finderNext.Click += (_, _) => NavigateFinder(previous: false);

        var counter = new StackPanel { MinWidth = 64, VerticalAlignment = VerticalAlignment.Center };
        _finderCounter = FinderLabel(14, "OnSurface", TextAlignment.Center);
        _finderCounter.FontWeight = FontWeights.Medium;
        AutomationProperties.SetAutomationId(_finderCounter, "GridSearchMatches");
        AutomationProperties.SetLiveSetting(_finderCounter, AutomationLiveSetting.Polite);
        _finderStatus = FinderLabel(10, "OnSurfaceVariant", TextAlignment.Center);
        _finderScope = FinderLabel(10, "OnSurfaceVariant", TextAlignment.Center);
        counter.Children.Add(_finderCounter);
        counter.Children.Add(_finderStatus);
        counter.Children.Add(_finderScope);

        var searchIcon = Material.Icon("Search");
        searchIcon.Margin = new Thickness(10, 0, 8, 0);
        var divider = new Border { Width = 1, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        Material.Paint(divider, Border.BackgroundProperty, "OutlineVariant");
        var fieldLayout = FinderColumns(searchIcon, input, _finderClear, divider, counter, _finderPrevious, _finderNext);
        var field = new System.Windows.Controls.Grid { Height = 40 };        var fieldBackground = new Border { CornerRadius = new CornerRadius(18) };        Material.Paint(fieldBackground, Border.BackgroundProperty, "SurfaceContainerHighest");        var fieldOutline = new Border        {            CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(2), IsHitTestVisible = false,            Style = Material.Style("FinderFieldOutline")        };        field.Children.Add(fieldBackground);        field.Children.Add(fieldLayout);        field.Children.Add(fieldOutline);

        var optionsLabel = FinderLabel(14, "OnSurface");
        optionsLabel.Text = "Search options";
        optionsLabel.FontWeight = FontWeights.Medium;
        var optionsContent = new StackPanel { Orientation = Orientation.Horizontal };
        optionsContent.Children.Add(optionsLabel);
        _finderOptionsChevron = Material.Icon("ChevronDown");
        _finderOptionsChevron.Margin = new Thickness(8, 0, 0, 0);
        optionsContent.Children.Add(_finderOptionsChevron);
        _finderFilterIcon = Material.Icon("FilterActive");
        optionsContent.Children.Add(_finderFilterIcon);
        var options = _finderOptions = Material.Button("Search options", "OutlinedButton");
        options.Content = optionsContent;
        options.Width = 144;
        options.Padding = new Thickness(0);
        options.ToolTip = "Search options";
        Material.Paint(options, Control.BorderBrushProperty, "OutlineVariant");
        AutomationProperties.SetName(options, "Search options");

        var filterLabel = FinderLabel(12, "OnSecondaryContainer");
        filterLabel.Text = "Filtering rows";
        var filterIcon = Material.Icon("Filter", role: "OnSecondaryContainer");
        var filterContent = new StackPanel { Orientation = Orientation.Horizontal };
        filterContent.Children.Add(filterIcon);
        filterContent.Children.Add(filterLabel);
        _finderFilterIndicator = new Border
        {
            Height = 28, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 0, 8, 0),
            ToolTip = "Filtering rows", Child = filterContent, Margin = new Thickness(8, 0, 0, 0)
        };
        filterLabel.Margin = new Thickness(4, 0, 0, 0);
        AutomationProperties.SetName(_finderFilterIndicator, "Filtering rows");
        Material.Paint(_finderFilterIndicator, Border.BackgroundProperty, "SecondaryContainer");

        var settings = Material.IconButton("Settings", "Grid settings", 40);
        var close = Material.IconButton("Close", "Close Find", 40);
        close.Click += (_, _) => CloseFinder();
        _searchOptionsMenu = AttachFinderMenu(options, FinderOptions, id => new FinderOptionCommand(this, id), finderOptions: true);
        _gridSettingsMenu = AttachFinderMenu(settings, FinderGridSettings,
            id => MenuCommandResolver?.Invoke(id) ?? new GridMenuCommand(this, id));

        options.Margin = settings.Margin = close.Margin = new Thickness(8, 0, 0, 0);
        identity.Margin = new Thickness(0, 0, 8, 0);
        var content = FinderColumns(identity, field, _finderFilterIndicator, options, settings, close);
        var bar = new Border
        {
            Height = 56, Padding = new Thickness(16, 0, 16, 0), Child = content, ClipToBounds = true,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Visibility = IsSearchPanelVisible ? Visibility.Visible : Visibility.Collapsed
        };
        Material.Paint(bar, Border.BackgroundProperty, "SurfaceContainerLow");
        Material.Paint(bar, Border.BorderBrushProperty, "OutlineVariant");

        bar.SizeChanged += (_, e) =>
        {
            double width = e.NewSize.Width;
            bool compact = width < 680;
            options.Width = compact ? 40 : 144;
            optionsLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            _finderOptionsChevron.Margin = compact ? new Thickness(0) : new Thickness(8, 0, 0, 0);
            filterLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            identity.Visibility = width < 460 ? Visibility.Collapsed : Visibility.Visible;
            identity.Width = compact ? 56 : 72;
            searchIcon.Visibility = width < 380 ? Visibility.Collapsed : Visibility.Visible;
            bar.Padding = width < 380 ? new Thickness(8, 0, 8, 0) : new Thickness(16, 0, 16, 0);
            _finderCondensed = width < 420;
            UpdateFinderFilterIndicator();
        };

        UpdateFinderStatus(pendingFilter: false);
        return bar;
    }

    private static System.Windows.Controls.Grid FinderColumns(params FrameworkElement[] children)
    {
        var grid = new System.Windows.Controls.Grid();
        for (int index = 0; index < children.Length; index++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = index == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            System.Windows.Controls.Grid.SetColumn(children[index], index);
            grid.Children.Add(children[index]);
        }
        return grid;
    }

    private static TextBlock FinderLabel(double size, string role, TextAlignment alignment = TextAlignment.Left)
    {
        var label = new TextBlock
        {
            FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextAlignment = alignment,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Material.Paint(label, TextBlock.ForegroundProperty, role);
        return label;
    }

    private ContextMenu AttachFinderMenu(Button button, Func<ImmutableArray<MenuEntry>> items, Func<string, ICommand?> commands,
        bool finderOptions = false)
    {
        var menu = MenuRenderer.Attach(button, () => new MenuOpeningEventArgs(new MenuHit(MenuRegion.ColumnHeader), items()),
            commands, finderOptions);
        TrackGridMenu(menu);
        menu.Placement = PlacementMode.Bottom;
        menu.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            menu.IsOpen = false;
            button.Focus();
            e.Handled = true;
        };
        button.Click += (_, _) => menu.IsOpen = true;
        return menu;
    }

    private ImmutableArray<MenuEntry> FinderOptions() =>
    [
        new MenuSection("Mode"),
        new MenuCommand("find-highlight", "Highlight matches", "Search", IsChecked: !_finderFilterRows),
        new MenuCommand("find-filter", "Filter matching rows", "Filter", IsChecked: _finderFilterRows),
        new MenuSection("Scope"),
        new MenuCommand("find-visible", "Visible columns", "Columns", IsEnabled: false, IsChecked: true),
        new MenuSeparator(),
        new MenuCommand("find-case", "Case sensitive", IsChecked: _finderCaseSensitive),
        new MenuCommand("find-word", "Match whole word", IsChecked: _finderWholeWord)
    ];

    private ImmutableArray<MenuEntry> FinderGridSettings() =>
    [
        new MenuCommand("group-panel", "Show group panel", "Group", IsChecked: ShowGroupPanel),
        new MenuCommand("columns", "Column Chooser", "Columns"),
        new MenuCommand("best-fit-all", "Best fit (all columns)", "BestFit", IsEnabled: _displayColumns.Count > 0)
    ];

    private sealed class FinderOptionCommand(MatrixGrid grid, string id) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => id != "find-visible";

        public void Execute(object? parameter)
        {
            switch (id)
            {
                case "find-highlight": grid._finderFilterRows = false; break;
                case "find-filter": grid._finderFilterRows = true; break;
                case "find-case": grid._finderCaseSensitive = !grid._finderCaseSensitive; break;
                case "find-word": grid._finderWholeWord = !grid._finderWholeWord; break;
            }
            grid.ApplySearch();
        }
    }

    private void UpdateFinderStatus(bool pendingFilter)
    {
        if (_finderCounter is not { } counter)
            return;
        bool hasText = !string.IsNullOrWhiteSpace(_search?.Text);
        bool pending = _searchTimer.IsEnabled || _finderScan is not null || pendingFilter;
        int count = _finderMatches.Count;
        var culture = CultureInfo.CurrentCulture;
        var progress = !hasText || pending || _finderSearchFailed ? "— / —"
            : $"{(_finderIndex + 1).ToString("N0", culture)} / {count.ToString("N0", culture)}";
        var status = !hasText ? string.Empty
            : _finderSearchFailed ? "Search failed"
            : pending ? pendingFilter && EditingCell is not null ? "Finish editing" : string.Empty
            : count == 0 ? "No results" : string.Empty;
        var scope = Provider is null ? string.Empty : "Loaded rows";
        counter.Text = progress;
        SetFinderCaption(_finderStatus!, status);
        SetFinderCaption(_finderScope!, status.Length == 0 ? scope : string.Empty);
        _finderTitle!.Text = DocumentTitle;
        _finderTitle.ToolTip = DocumentTitle;
        _finderRowCount!.Text = ResultRowCount is int total
            ? $"{total.ToString("N0", culture)} {(total == 1 ? "row" : "rows")}"
            : $"{VisibleRows.Count(row => row.Group is null).ToString("N0", culture)} loaded";
        _finderRowCount.ToolTip = _finderRowCount.Text;
        _finderClear!.IsEnabled = _search?.Text.Length > 0;
        _finderPrevious!.IsEnabled = _finderNext!.IsEnabled = !pending && !_finderSearchFailed && count > 1 && EditingCell is null;
        UpdateFinderFilterIndicator();

        string result = !hasText ? "Empty query" : status.Length > 0 ? status
            : pending ? "Searching" : $"Match {_finderIndex + 1} of {count}";
        string announcement = string.Join(". ", new[] { result, scope }
            .Where(part => part.Length != 0));
        if (AutomationProperties.GetName(counter) == announcement)
            return;
        AutomationProperties.SetName(counter, announcement);
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            (UIElementAutomationPeer.FromElement(counter) ?? UIElementAutomationPeer.CreatePeerForElement(counter))
                ?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void UpdateFinderFilterIndicator()
    {
        bool filtering = _finderFilterRows && !string.IsNullOrWhiteSpace(_search?.Text);
        _finderFilterIndicator!.Visibility = filtering && !_finderCondensed ? Visibility.Visible : Visibility.Collapsed;
        _finderFilterIcon!.Visibility = filtering && _finderCondensed ? Visibility.Visible : Visibility.Collapsed;
        _finderOptionsChevron!.Visibility = _finderFilterIcon.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        _finderOptions!.ToolTip = filtering && _finderCondensed ? "Search options · Filtering rows" : "Search options";
        AutomationProperties.SetHelpText(_finderOptions, filtering ? "Filtering rows" : string.Empty);
    }

    private static void SetFinderCaption(TextBlock label, string text)
    {
        label.Text = text;
        label.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
