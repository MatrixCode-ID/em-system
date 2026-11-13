using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Em.Editors;
using Em.Ui.Wpf.Controls;

namespace Em.Ui.Wpf.Controls;

/// <summary>A lookup editor whose popup searches the options as the user types and shows extra fields in columns.</summary>
public sealed class LookupEditor : ValueEditor
{
    private const int PageSize = 100;
    private TextBox _input = null!;
    private TextBox _search = null!;
    private ListView _list = null!;
    private TextBlock _status = null!;
    private Button _previous = null!;
    private Button _next = null!;
    private Popup _popup = null!;
    private CancellationTokenSource? _request;
    private EditorOption? _selected;
    private int _offset;
    private LookupSettings Options => (LookupSettings)Settings;

    static LookupEditor() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(LookupEditor), new FrameworkPropertyMetadata(typeof(LookupEditor)));

    /// <summary>Creates a lookup editor with default lookup settings.</summary>
    public LookupEditor() : base(new LookupSettings()) { }

    /// <summary>Gets the column headers for the option search fields shown in the popup.</summary>
    public IReadOnlyList<string> FieldHeaders { get; init; } = ["Details"];

    /// <inheritdoc/>
    protected override FrameworkElement CreateEditor()
    {
        _input = CreateTextBox(readOnly: true);
        _search = new TextBox { Style = Material.Style("SearchField"), Margin = new Thickness(0, 0, 0, 8) };
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search choices");
        _search.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurface");
        _search.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLowest");
        _search.TextChanged += (_, _) => { if (_popup.IsOpen) StartSearch(0); };

        var view = new GridView { ColumnHeaderContainerStyle = Material.Style("LookupHeader") };
        view.Columns.Add(new GridViewColumn { Header = "Name", Width = 160, DisplayMemberBinding = new Binding(nameof(ChoiceRow.Label)) });
        for (int index = 0; index < FieldHeaders.Count; index++)
            view.Columns.Add(new GridViewColumn { Header = FieldHeaders[index], Width = 120, DisplayMemberBinding = new Binding($"Fields[{index}]") });
        _list = new ListView { View = view, Height = 220, MinWidth = 320,
            BorderThickness = new Thickness(0), ItemContainerStyle = Material.Style("ListViewItem") };
        _list.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurface");
        _list.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.SurfaceContainerLowest");
        VirtualizingPanel.SetIsVirtualizing(_list, true);
        VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_list, true);
        _list.MouseDoubleClick += (_, e) =>
        {
            if (ItemsControl.ContainerFromElement(_list, e.OriginalSource as DependencyObject) is ListViewItem)
                SelectChoice();
        };

        _status = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        _status.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurface");
        _previous = CreateButton("‹", "Previous result page", (_, _) => StartSearch(Math.Max(0, _offset - PageSize)), "ChevronLeft");
        _next = CreateButton("›", "Next result page", (_, _) => StartSearch(_offset + PageSize), "ChevronRight");
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(_status);
        actions.Children.Add(_previous);
        actions.Children.Add(_next);
        var select = Material.Button("Select", "FilledButton");
        System.Windows.Automation.AutomationProperties.SetName(select, "Select choice");
        select.Click += (_, _) => SelectChoice();
        actions.Children.Add(select);

        var body = new StackPanel();
        body.Children.Add(_search);
        body.Children.Add(_list);
        body.Children.Add(actions);
        body.PreviewKeyDown += OnPopupKey;
        var card = Material.Panel(body, "SurfaceContainer", 12, new Thickness(12));
        card.Margin = new Thickness(0, 0, 10, 10);
        _popup = new Popup { PlacementTarget = _input, StaysOpen = false, Child = card, AllowsTransparency = true };
        _popup.Opened += (_, _) =>
        {
            GridTheme.SetMode(card, GridTheme.GetMode(this));
            StartSearch(0);
            _search.Focus();
        };
        _popup.Closed += (_, _) => CancelSearch();
        var panel = WithButtons(_input,
            CreateButton("▾", "Open lookup", (_, _) => TogglePopup(), "ChevronDown"),
            CreateButton("×", "Clear selection", (_, _) => { _selected = null; Clear(); }, "Close"));
        DockPanel.SetDock(_popup, Dock.Right);
        panel.Children.Insert(0, _popup);
        Unloaded += OnUnloaded;
        return panel;
    }

    /// <inheritdoc/>
    protected override void RefreshEditor()
    {
        _input.Text = _selected is { } option && Equals(option.Value, Value)
            ? option.Label : Settings.Format(Value);
    }

    /// <inheritdoc/>
    protected override void ReleaseEditor()
    {
        _popup.IsOpen = false;
        CancelSearch();
        Unloaded -= OnUnloaded;
    }

    /// <summary>Opens the popup on Alt+Down or F4 and moves through the matches with the arrow keys.</summary>
    /// <param name="e">The key event data.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F4 || e.Key == Key.System && e.SystemKey == Key.Down)
        {
            TogglePopup();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void OnPopupKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F4)
        {
            _popup.IsOpen = false;
            _input.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Down && _search.IsKeyboardFocusWithin && _list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
            _list.ScrollIntoView(_list.SelectedItem);
            if (_list.ItemContainerGenerator.ContainerFromIndex(0) is ListViewItem first)
                first.Focus();
            else
                _list.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            SelectChoice();
            e.Handled = true;
        }
    }

    private void SelectChoice()
    {
        if (_list.SelectedItem is not ChoiceRow row)
            return;
        var result = Settings.ConvertAndValidate(row.Option.Value, ValueType);
        if (result.IsValid)
            _selected = row.Option;
        if (Accept(result))
        {
            _popup.IsOpen = false;
            if (Host is null)
                _input.Focus();
        }
    }

    private void TogglePopup()
    {
        if (!Settings.IsReadOnly)
            _popup.IsOpen = !_popup.IsOpen;
    }

    private async void StartSearch(int offset)
    {
        CancelSearch();
        var source = _request = new CancellationTokenSource();
        var token = source.Token;
        var options = Options;
        var query = _search.Text;
        _status.Text = "Searching…";
        _list.ItemsSource = null;
        _previous.IsEnabled = _next.IsEnabled = false;
        try
        {
            await Task.Delay(120, token);
            var rows = await Task.Run(async () => await options.SearchAsync(query, offset, PageSize + 1, token), token);
            if (!ReferenceEquals(_request, source))
                return;
            _offset = offset;
            _list.ItemsSource = rows.Take(PageSize).Select(option => new ChoiceRow(option, FieldHeaders.Count)).ToArray();
            _status.Text = rows.Count == 0 ? "No results" : $"{offset + 1}–{offset + Math.Min(PageSize, rows.Count)}";
            _previous.IsEnabled = offset > 0;
            _next.IsEnabled = rows.Count > PageSize;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            if (ReferenceEquals(_request, source))
                _status.Text = "Search unavailable";
        }
        finally
        {
            if (ReferenceEquals(_request, source))
                _request = null;
            source.Dispose();
        }
    }

    private void CancelSearch()
    {
        var previous = _request;
        _request = null;
        previous?.Cancel();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _popup.IsOpen = false;

    private sealed class ChoiceRow(EditorOption option, int fieldCount)
    {
        public EditorOption Option { get; } = option;
        public string Label => Option.Label;
        public string[] Fields { get; } = Enumerable.Range(0, fieldCount)
            .Select(index => index < (option.SearchFields.IsDefault ? 0 : option.SearchFields.Length)
                ? option.SearchFields[index] : string.Empty).ToArray();
    }
}
