using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Editors;
using Em.Controls.Grid.Menus;

namespace Em.Ui.Wpf.Controls;

/// <summary>A single-line text editor with optional input mask, used standalone and inside grid cells.</summary>
[TemplatePart(Name = InputPart, Type = typeof(TextBox))]
public sealed class TextEditor : Control, IGridEditor
{
    private const string InputPart = "PART_Input";
    private TextBox? _input;

    /// <summary>Identifies the <see cref="Value"/> dependency property.</summary>
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(TextEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, Refresh));

    /// <summary>Identifies the <see cref="Settings"/> dependency property.</summary>
    public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
        nameof(Settings), typeof(TextSettings), typeof(TextEditor),
        new PropertyMetadata(new TextSettings(), Refresh), value => value is TextSettings);

    private static readonly DependencyPropertyKey ErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Error), typeof(string), typeof(TextEditor), new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="Error"/> dependency property.</summary>
    public static readonly DependencyProperty ErrorProperty = ErrorPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasErrorPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(HasError), typeof(bool), typeof(TextEditor), new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="HasError"/> dependency property.</summary>
    public static readonly DependencyProperty HasErrorProperty = HasErrorPropertyKey.DependencyProperty;

    static TextEditor() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(TextEditor), new FrameworkPropertyMetadata(typeof(TextEditor)));

    /// <summary>Gets or sets the text. Binds two-way by default.</summary>
    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Gets or sets the text settings.</summary>
    public TextSettings Settings
    {
        get => (TextSettings)GetValue(SettingsProperty);
        set => SetValue(SettingsProperty, value);
    }

    /// <summary>Gets the validation message of the last rejected input, or null.</summary>
    public string? Error => (string?)GetValue(ErrorProperty);
    /// <summary>Gets whether the last input was rejected.</summary>
    public bool HasError => (bool)GetValue(HasErrorProperty);
    /// <summary>Gets the formatted text of the value.</summary>
    public string DisplayText => Settings.Format(Value);

    /// <summary>Gets or sets the grid that hosts the editor in a cell, or null for a standalone editor.</summary>
    public IInPlaceEditorHost? Host { get; set; }
    /// <summary>Gets or sets a resolver that maps menu command identifiers to commands for the editor context menu.</summary>
    public Func<string, ICommand?>? MenuCommandResolver { get; set; }
    /// <summary>Occurs before the editor context menu opens, so handlers can change its items.</summary>
    public event EventHandler<MenuOpeningEventArgs>? EditorMenuOpening;

    Control IGridEditor.Element => this;
    object? IGridEditor.EditValue { get => Value; set => Value = (string?)value; }

    /// <summary>Moves keyboard focus into the text box.</summary>
    /// <returns>True when focus moved.</returns>
    public bool FocusEditor()
    {
        ApplyTemplate();
        return _input?.Focus() ?? false;
    }

    /// <summary>Connects the template text box.</summary>
    public override void OnApplyTemplate()
    {
        if (_input is not null)
        {
            _input.PreviewKeyDown -= OnInputKey;
            _input.GotKeyboardFocus -= OnInputFocused;
            _input.LostKeyboardFocus -= OnInputBlurred;
        }
        base.OnApplyTemplate();
        _input = GetTemplateChild(InputPart) as TextBox;
        if (_input is not null)
        {
            _input.PreviewKeyDown += OnInputKey;
            _input.GotKeyboardFocus += OnInputFocused;
            _input.LostKeyboardFocus += OnInputBlurred;
            EditorMenu.Attach(_input, _input, () => Settings.AllowNull && !Settings.IsReadOnly,
                () => { _input.Text = string.Empty; Commit(); },
                opening => EditorMenuOpening?.Invoke(this, opening), id => MenuCommandResolver?.Invoke(id));
        }
        ResetInput();
    }

    /// <summary>Validates and stores the typed text and notifies the host.</summary>
    /// <returns>True when the text was accepted.</returns>
    public bool Commit()
    {
        if (_input is null || Settings.IsReadOnly)
            return false;
        var result = Settings.Parse(_input.Text, typeof(string));
        SetValue(ErrorPropertyKey, result.Error);
        SetValue(HasErrorPropertyKey, !result.IsValid);
        if (!result.IsValid)
            return false;

        SetCurrentValue(ValueProperty, (string?)result.Value);
        ResetInput();
        Host?.Commit(result.Value);
        return true;
    }

    /// <summary>Discards the typed text and notifies the host.</summary>
    public void Cancel()
    {
        ResetInput();
        Host?.Cancel();
    }

    private static void Refresh(DependencyObject source, DependencyPropertyChangedEventArgs _) =>
        ((TextEditor)source).ResetInput();

    private void ResetInput()
    {
        SetValue(ErrorPropertyKey, null);
        SetValue(HasErrorPropertyKey, false);
        if (_input is null)
            return;
        _input.IsReadOnly = Settings.IsReadOnly;
        _input.MaxLength = Settings.MaxLength ?? 0;
        _input.Text = _input.IsKeyboardFocused ? Value ?? string.Empty : DisplayText;
    }

    private void OnInputFocused(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (Error is null)
            ResetInput();
        if (Settings.SelectAllOnFocus)
            _input!.SelectAll();
    }

    private void OnInputBlurred(object sender, KeyboardFocusChangedEventArgs args)
    {
        if (Host is null)
            Commit();
    }

    private void OnInputKey(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            Commit();
            args.Handled = true;
        }
        else if (args.Key == Key.Tab && Host is { } host && Commit())
            args.Handled = host.Navigate(args.Key, Keyboard.Modifiers);
        else if (args.Key == Key.Escape)
        {
            Cancel();
            args.Handled = true;
        }
    }
}
