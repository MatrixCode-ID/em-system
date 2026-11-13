using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Em.Editors;
using Em.Controls.Grid.Menus;
using Em.Ui.Wpf.Controls;

namespace Em.Ui.Wpf.Controls;

/// <summary>Base of the standalone and in-cell value editors. Parsing, validation and formatting come from the editor settings.</summary>
[TemplatePart(Name = "PART_Host", Type = typeof(ContentPresenter))]
public abstract class ValueEditor : Control, IGridEditor
{
    /// <summary>Identifies the <see cref="Value"/> dependency property.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(object), typeof(ValueEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    /// <summary>Identifies the <see cref="Settings"/> dependency property.</summary>
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
        nameof(Settings), typeof(EditorSettings), typeof(ValueEditor),
        new FrameworkPropertyMetadata(null, OnSettingsChanged));

    /// <summary>Identifies the <see cref="ValueType"/> dependency property.</summary>
    public static readonly DependencyProperty ValueTypeProperty = DependencyProperty.Register(
        nameof(ValueType), typeof(Type), typeof(ValueEditor), new PropertyMetadata(typeof(object)),
        value => value is Type);

    private static readonly DependencyPropertyKey ErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Error), typeof(string), typeof(ValueEditor), new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="Error"/> dependency property.</summary>
    public static readonly DependencyProperty ErrorProperty = ErrorPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasError), typeof(bool), typeof(ValueEditor), new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="HasError"/> dependency property.</summary>
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    /// <summary>Initializes the editor with its default settings.</summary>
    /// <param name="defaultSettings">The settings used until others are set.</param>
    protected ValueEditor(EditorSettings defaultSettings)
    {
        SetCurrentValue(SettingsProperty, defaultSettings);
    }

    /// <summary>Gets or sets the edited value. Binds two-way by default.</summary>
    public object? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Gets or sets the settings that parse, validate and format the value.</summary>
    public EditorSettings Settings
    {
        get => (EditorSettings)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>Gets or sets the type the value is converted to on commit. The default is object.</summary>
    public Type ValueType
    {
        get => (Type)GetValue(ValueTypeProperty);
        set => SetValue(ValueTypeProperty, value);
    }

    /// <summary>Gets the validation message of the last rejected input, or null.</summary>
    public string? Error => (string?)GetValue(ErrorProperty);
    /// <summary>Gets whether the last input was rejected.</summary>
    public bool HasError => (bool)GetValue(HasErrorProperty);
    /// <summary>Gets or sets the grid that hosts the editor in a cell, or null for a standalone editor.</summary>
    public IInPlaceEditorHost? Host { get; set; }
    /// <summary>Gets or sets a resolver that maps menu command identifiers to commands for the editor context menu.</summary>
    public Func<string, ICommand?>? MenuCommandResolver { get; set; }

    /// <summary>Occurs before the editor context menu opens, so handlers can change its items.</summary>
    public event EventHandler<MenuOpeningEventArgs>? EditorMenuOpening;

    Control IGridEditor.Element => this;
    object? IGridEditor.EditValue { get => Value; set => Value = value; }

    /// <summary>Gets the element created by <see cref="CreateEditor"/>.</summary>
    protected FrameworkElement? EditorElement { get; private set; }
    /// <summary>Gets or sets the element that receives keyboard focus.</summary>
    protected FrameworkElement? InputElement { get; set; }
    /// <summary>Gets whether the editor is refreshing its element from the value, so change handlers can ignore the update.</summary>
    protected bool Updating { get; private set; }

    /// <summary>Rebuilds the editor element when the template is applied.</summary>
    public override void OnApplyTemplate()
    {
        TearDown();
        base.OnApplyTemplate();
        Build();
    }

    /// <summary>Validates and stores the current value and notifies the host.</summary>
    /// <returns>True when the value was accepted.</returns>
    public virtual bool Commit() => Accept(Settings.ConvertAndValidate(Value, ValueType));
    /// <summary>Discards pending input and notifies the host.</summary>
    public virtual void Cancel()
    {
        Reset();
        Host?.Cancel();
    }

    /// <summary>Moves keyboard focus into the editor.</summary>
    /// <returns>True when focus moved.</returns>
    public bool FocusEditor()
    {
        ApplyTemplate();
        return InputElement?.Focus() ?? false;
    }

    /// <summary>Commits on Enter, cancels on Escape and lets the host move to the next cell on Tab.</summary>
    /// <param name="e">The key event data.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled)
            return;

        if (e.Key == Key.Enter)
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && Host is { } host && Commit())
            e.Handled = host.Navigate(e.Key, Keyboard.Modifiers);
    }

    /// <summary>Creates the editor element.</summary>
    /// <returns>The element.</returns>
    protected abstract FrameworkElement CreateEditor();
    /// <summary>Updates the editor element from the current value.</summary>
    protected abstract void RefreshEditor();
    /// <summary>Releases resources of the editor element before it is replaced.</summary>
    protected virtual void ReleaseEditor() { }

    /// <summary>Sets or clears the validation message.</summary>
    /// <param name="message">The message, or null to clear it.</param>
    protected void SetError(string? message)
    {
        SetValue(ErrorPropertyKey, message);
        SetValue(HasErrorPropertyKey, message is not null);
    }

    /// <summary>Applies a parse or validation result: shows its error, or stores the value and notifies the host.</summary>
    /// <param name="result">The result.</param>
    /// <param name="notifyHost">Whether the host is told about the new value.</param>
    /// <returns>True when the value was accepted.</returns>
    protected bool Accept(EditorResult result, bool notifyHost = true)
    {
        SetError(result.Error);
        if (!result.IsValid)
            return false;
        SetCurrentValue(ValueProperty, result.Value);
        Reset();
        if (notifyHost)
            Host?.Commit(result.Value);
        return true;
    }

    /// <summary>Stores null when the settings allow it.</summary>
    /// <returns>True when the value was cleared.</returns>
    protected bool Clear() => Accept(Settings.ConvertAndValidate(null, ValueType));

    /// <summary>Creates a borderless Material text box and makes it the focus element.</summary>
    /// <param name="readOnly">Whether typing is blocked.</param>
    /// <returns>The text box.</returns>
    protected TextBox CreateTextBox(bool readOnly = false)
    {
        var input = new TextBox
        {
            Style = Material.Style("BareInput"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = System.Windows.Media.Brushes.Transparent,
            IsReadOnly = readOnly
        };
        input.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurface");
        input.FontFamily = FontFamily;
        input.FontSize = FontSize;
        InputElement = input;
        return input;
    }

    /// <summary>Creates a text button that does not take focus.</summary>
    /// <param name="text">The caption.</param>
    /// <param name="name">The accessible name.</param>
    /// <param name="click">The click handler.</param>
    /// <returns>The button.</returns>
    protected Button CreateButton(string text, string name, RoutedEventHandler click) =>
        CreateButton(text, name, click, null);

    /// <summary>Width this editor needs to show its content. The grid widens the in-cell editor to it when the column is narrower.</summary>
    public virtual double PreferredWidth => 0;

    /// <summary>Creates a text or icon button that does not take focus and is disabled for read-only settings.</summary>
    /// <param name="text">The caption for a text button.</param>
    /// <param name="name">The accessible name.</param>
    /// <param name="click">The click handler.</param>
    /// <param name="icon">The icon key, or null for a text button.</param>
    /// <param name="size">The icon button size.</param>
    /// <returns>The button.</returns>
    protected Button CreateButton(string text, string name, RoutedEventHandler click, string? icon, double size = 28)
    {
        var button = icon is null ? Material.Button(text) : Material.IconButton(icon, name, size);
        if (icon is not null && size < 28)
            button.Content = Material.Icon(icon, 16);
        button.Focusable = false;
        button.IsTabStop = false;
        button.IsEnabled = !Settings.IsReadOnly;
        System.Windows.Automation.AutomationProperties.SetName(button, name);
        button.Click += click;
        return button;
    }

    /// <summary>Docks buttons to the right of an input.</summary>
    /// <param name="input">The input element.</param>
    /// <param name="buttons">The buttons, in visual order.</param>
    /// <returns>The panel.</returns>
    protected DockPanel WithButtons(FrameworkElement input, params FrameworkElement[] buttons)
    {
        var panel = new DockPanel();
        foreach (var button in buttons.Reverse())
        {
            DockPanel.SetDock(button, Dock.Right);
            panel.Children.Add(button);
        }
        panel.Children.Add(input);
        return panel;
    }

    /// <summary>Clears the error and refreshes the editor element from the value.</summary>
    protected void Reset()
    {
        SetError(null);
        if (EditorElement is null)
            return;
        Updating = true;
        try { RefreshEditor(); }
        finally { Updating = false; }
    }

    private void Build()
    {
        if (GetTemplateChild("PART_Host") is not ContentPresenter presenter)
            return;
        EditorElement = CreateEditor();
        presenter.Content = EditorElement;
        var menuOwner = InputElement ?? EditorElement;
        EditorMenu.Attach(menuOwner, InputElement as TextBox,
            () => Settings.AllowNull && !Settings.IsReadOnly,
            () => Clear(),
            opening => EditorMenuOpening?.Invoke(this, opening),
            id => MenuCommandResolver?.Invoke(id));
        Reset();
    }

    private void TearDown()
    {
        if (EditorElement is not null)
            ReleaseEditor();
        EditorElement = null;
        InputElement = null;
    }

    private static void OnValueChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        ((ValueEditor)target).Reset();

    private static void OnSettingsChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        var editor = (ValueEditor)target;
        if (editor.EditorElement is null)
            return;
        editor.TearDown();
        editor.Build();
    }
}
