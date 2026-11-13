using System.Windows;
using System.Windows.Controls;
using Em.Editors;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents.Editors;

/// <summary>A check box editor. Values default to nullable boolean.</summary>
public sealed class CheckEditor : ValueEditor
{
    private CheckBox _check = null!;

    static CheckEditor() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(CheckEditor), new FrameworkPropertyMetadata(typeof(CheckEditor)));

    /// <summary>Creates a check box editor with default check settings.</summary>
    public CheckEditor() : base(new CheckSettings()) => ValueType = typeof(bool?);

    /// <inheritdoc/>
    protected override FrameworkElement CreateEditor()
    {
        _check = new CheckBox
        {
            Style = Material.Style("CheckBox"),
            // The 18-DIP box lines up with the cell text at 12 DIPs. The 7-DIP padding gives the hover and focus state layer
            // a 32-DIP square around the box instead of letting it stretch across the whole cell.
            Padding = new Thickness(7),
            VerticalAlignment = VerticalAlignment.Center,
            IsThreeState = CanSetNull,
            IsEnabled = !Settings.IsReadOnly
        };
        System.Windows.Automation.AutomationProperties.SetName(_check, "Check value");
        _check.Click += (_, _) =>
        {
            if (!Updating && !Accept(Settings.ConvertAndValidate(_check.IsChecked, ValueType)))
                _check.IsChecked = Value as bool?;
        };
        ApplyAlignment();
        InputElement = _check;
        return _check;
    }

    // The box follows HorizontalContentAlignment (the grid sets it from the column): left lines up with the cell text at 12,
    // center and right keep the state layer's 5-DIP inset from the edge.
    private void ApplyAlignment()
    {
        if (_check is null)
            return;
        var alignment = HorizontalContentAlignment == HorizontalAlignment.Stretch ? HorizontalAlignment.Left : HorizontalContentAlignment;
        _check.HorizontalAlignment = alignment;
        _check.Margin = alignment switch
        {
            HorizontalAlignment.Center => new Thickness(0),
            HorizontalAlignment.Right => new Thickness(0, 0, 5, 0),
            _ => new Thickness(5, 0, 0, 0)
        };
    }

    /// <inheritdoc/>
    protected override void RefreshEditor() => _check.IsChecked = Value as bool?;

    /// <summary>Allows the indeterminate state for nullable value types and applies content alignment.</summary>
    /// <param name="e">The property change data.</param>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ValueTypeProperty && _check is not null)
            _check.IsThreeState = CanSetNull;
        else if (e.Property == HorizontalContentAlignmentProperty)
            ApplyAlignment();
    }

    private bool CanSetNull => Settings.AllowNull && (!ValueType.IsValueType || Nullable.GetUnderlyingType(ValueType) is not null);
}
