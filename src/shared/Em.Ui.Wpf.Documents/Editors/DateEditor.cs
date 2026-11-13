using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Em.Editors;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Editors;

/// <summary>A date editor with a text box and a calendar drop-down. Values default to nullable DateOnly.</summary>
public sealed class DateEditor : ValueEditor
{
    private TextBox _input = null!;
    private Calendar _calendar = null!;
    private Popup _popup = null!;
    private DateSettings Options => (DateSettings)Settings;

    static DateEditor() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(DateEditor), new FrameworkPropertyMetadata(typeof(DateEditor)));

    /// <summary>Creates a date editor with default date settings.</summary>
    public DateEditor() : base(new DateSettings()) => ValueType = typeof(DateOnly?);

    /// <inheritdoc/>
    protected override FrameworkElement CreateEditor()
    {
        _input = CreateTextBox(Settings.IsReadOnly);
        _input.GotKeyboardFocus += (_, _) => { if (Error is null) Reset(); };
        _input.LostKeyboardFocus += (_, _) => { if (Host is null && !_popup.IsOpen) Commit(); };
        _calendar = new Calendar
        {
            Style = Material.Style("Calendar"),
            DisplayDateStart = Options.Minimum?.ToDateTime(TimeOnly.MinValue),
            DisplayDateEnd = Options.Maximum?.ToDateTime(TimeOnly.MinValue)
        };
        _calendar.SelectedDatesChanged += (_, _) =>
        {
            if (!Updating && _calendar.SelectedDate is DateTime date)
                SelectDate(date);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        actions.Children.Add(CreateButton("Today", "Today", (_, _) => SelectDate(DateTime.Today)));
        if (Settings.AllowNull)
            actions.Children.Add(CreateButton("Clear", "Clear date", (_, _) => { if (Clear()) ClosePopup(); }));
        var body = new StackPanel();
        body.Children.Add(_calendar);
        body.Children.Add(actions);
        var card = Material.Panel(body, "SurfaceContainer", 12, new Thickness(12));
        card.Margin = new Thickness(0, 0, 10, 10);
        _popup = new Popup { PlacementTarget = _input, StaysOpen = false, Child = card, AllowsTransparency = true };
        _popup.Opened += (_, _) =>
        {
            GridTheme.SetMode(card, GridTheme.GetMode(this));
            _calendar.DisplayMode = CalendarMode.Month;
            _calendar.DisplayDate = _calendar.SelectedDate ?? DateTime.Today;
        };
        // Calendar keeps mouse capture after a period button click, which swallows the next click.
        _calendar.PreviewMouseUp += (_, _) =>
        {
            if (Mouse.Captured is CalendarItem)
                Mouse.Capture(null);
        };
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.F4)
            {
                ClosePopup();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && _calendar.SelectedDate is { } day)
            {
                SelectDate(day);
                e.Handled = true;
            }
        };
        var panel = WithButtons(_input, CreateButton("▾", "Open calendar", (_, _) => TogglePopup(), "Calendar"));
        DockPanel.SetDock(_popup, Dock.Right);
        panel.Children.Insert(0, _popup);
        Unloaded += OnUnloaded;
        return panel;
    }

    /// <inheritdoc/>
    protected override void ReleaseEditor()
    {
        _popup.IsOpen = false;
        Unloaded -= OnUnloaded;
    }

    /// <inheritdoc/>
    protected override void RefreshEditor()
    {
        var date = Value switch
        {
            DateOnly day => (DateTime?)day.ToDateTime(TimeOnly.MinValue),
            DateTime timestamp => timestamp,
            _ => null
        };
        _calendar.SelectedDate = date;
        _input.Text = _input.IsKeyboardFocused && Value is IFormattable value
            ? value.ToString(Options.EditFormat ?? (Options.IncludeTime ? "g" : "d"), Settings.Culture)
            : Settings.Format(Value);
    }

    /// <inheritdoc/>
    public override bool Commit() => Accept(Settings.Parse(_input.Text, ValueType));

    /// <summary>Opens the calendar on Alt+Down or F4 and handles commit and cancel keys.</summary>
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

    private void SelectDate(DateTime date)
    {
        var time = Value is DateTime current ? current.TimeOfDay : TimeSpan.Zero;
        var candidate = date.Date.Add(time);
        var day = DateOnly.FromDateTime(candidate);
        if (day < Options.Minimum || day > Options.Maximum)
        {
            SetError("The date is outside the allowed range.");
            return;
        }
        if (Accept(Settings.ConvertAndValidate(Options.IncludeTime ? (object)candidate : day, ValueType)))
            ClosePopup();
    }

    private void ClosePopup()
    {
        _popup.IsOpen = false;
        if (Host is null)
            _input.Focus();
    }

    private void TogglePopup()
    {
        if (Settings.IsReadOnly)
            return;
        if (_popup.IsOpen)
        {
            ClosePopup();
            return;
        }
        _popup.IsOpen = true;
        _calendar.Focus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _popup.IsOpen = false;
}
