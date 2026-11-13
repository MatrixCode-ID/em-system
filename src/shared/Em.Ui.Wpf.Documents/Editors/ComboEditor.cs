using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Em.Editors;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Editors;

/// <summary>A drop-down editor that chooses one of the options, optionally accepting typed values.</summary>
public sealed class ComboEditor : ValueEditor
{
    private ComboBox _combo = null!;
    private ComboSettings Options => (ComboSettings)Settings;

    static ComboEditor() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(ComboEditor), new FrameworkPropertyMetadata(typeof(ComboEditor)));

    /// <summary>Creates a combo editor with default combo settings.</summary>
    public ComboEditor() : base(new ComboSettings()) { }

    /// <inheritdoc/>
    protected override FrameworkElement CreateEditor()
    {
        _combo = new ComboBox
        {
            Style = Material.Style("ComboBox"),
            Height = double.NaN,
            ItemsSource = Options.Options,
            DisplayMemberPath = nameof(EditorOption.Label),
            SelectedValuePath = nameof(EditorOption.Value),
            IsEditable = Options.IsEditable,
            IsTextSearchEnabled = Options.AutoComplete,
            IsReadOnly = Settings.IsReadOnly,
            IsEnabled = !Settings.IsReadOnly,
            BorderThickness = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _combo.SetResourceReference(ForegroundProperty, "MatrixGrid.Brush.OnSurface");
        _combo.SetResourceReference(BackgroundProperty, "MatrixGrid.Brush.Surface");
        _combo.SelectionChanged += (_, _) =>
        {
            if (!Updating && _combo.SelectedItem is EditorOption option)
                Accept(Settings.ConvertAndValidate(option.Value, ValueType), notifyHost: false);
        };
        _combo.LostKeyboardFocus += (_, _) =>
        {
            if (Host is null && Options.IsEditable && !_combo.IsDropDownOpen && !_combo.IsKeyboardFocusWithin)
                Commit();
        };
        InputElement = _combo;
        return Settings.AllowNull ? WithButtons(_combo,
            CreateButton("×", "Clear selection", (_, _) => Clear(), "Close")) : _combo;
    }

    /// <inheritdoc/>
    protected override void RefreshEditor()
    {
        _combo.SelectedValue = Value;
        if (_combo.IsEditable)
            _combo.Text = Settings.Format(Value);
    }

    /// <summary>Accepts the selected option, or the typed text when the settings allow new values.</summary>
    /// <returns>True when the value was accepted.</returns>
    public override bool Commit()
    {
        if (_combo is null)
            return false;
        if (Options.IsEditable && _combo.Text != Settings.Format(Value))
            return Accept(Settings.Parse(_combo.Text, ValueType));
        return base.Commit();
    }

    /// <summary>Lets the open drop-down handle Enter and Escape; otherwise handles commit and cancel keys.</summary>
    /// <param name="e">The key event data.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_combo?.IsDropDownOpen == true && e.Key is Key.Enter or Key.Escape)
            return;
        base.OnPreviewKeyDown(e);
    }
}
