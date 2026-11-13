using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Em.Editors;

namespace Em.Ui.Wpf.Controls;

/// <summary>A numeric editor with spin buttons. Values default to nullable decimal.</summary>
public sealed class SpinEditor : ValueEditor
{
    private const double ButtonSize = 24;
    private TextBox _input = null!;
    private NumericSettings Options => (NumericSettings)Settings;

    static SpinEditor() => DefaultStyleKeyProperty.OverrideMetadata(
        typeof(SpinEditor), new FrameworkPropertyMetadata(typeof(SpinEditor)));

    /// <summary>Creates a spin editor with default numeric settings.</summary>
    public SpinEditor() : base(new NumericSettings()) => ValueType = typeof(decimal?);

    /// <inheritdoc/>
    protected override FrameworkElement CreateEditor()
    {
        _input = CreateTextBox(Settings.IsReadOnly);
        _input.GotKeyboardFocus += (_, _) => Reset();
        _input.LostKeyboardFocus += (_, _) => { if (Host is null) Commit(); };
        _input.PreviewMouseWheel += (_, e) =>
        {
            if (_input.IsKeyboardFocused && !Settings.IsReadOnly)
            {
                Step(Math.Sign(e.Delta));
                e.Handled = true;
            }
        };
        var increase = CreateButton("+", "Increase value", (_, _) => Step(1), "Plus", ButtonSize);
        increase.Margin = new Thickness(0, 0, 4, 0);
        return WithButtons(_input,
            CreateButton("−", "Decrease value", (_, _) => Step(-1), "Minus", ButtonSize), increase);
    }

    /// <inheritdoc/>
    public override double PreferredWidth
    {
        get
        {
            if (_input is null)
                return 0;
            // Widest of the current text, the formatted value and a five-digit sample, plus input padding, frame and both buttons.
            string text = new[] { _input.Text, Settings.Format(Value) ?? "", "00000" }.MaxBy(item => item.Length)!;
            var typeface = new Typeface(_input.FontFamily, _input.FontStyle, _input.FontWeight, _input.FontStretch);
            double textWidth = new FormattedText(text, Settings.Culture, FlowDirection, typeface, _input.FontSize,
                Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
            return Math.Ceiling(textWidth) + 16 + 4 + 2 * ButtonSize + 4 + 8;
        }
    }

    /// <inheritdoc/>
    protected override void RefreshEditor()
    {
        _input.Text = _input.IsKeyboardFocused && Value is IFormattable value
            ? value.ToString(Options.EditFormat ?? "G", Settings.Culture)
            : Settings.Format(Value);
    }

    /// <inheritdoc/>
    public override bool Commit() => Accept(Settings.Parse(_input.Text, ValueType));

    /// <summary>Steps the value with the Up and Down keys and handles commit and cancel keys.</summary>
    /// <param name="e">The key event data.</param>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!Settings.IsReadOnly && e.Key is Key.Up or Key.Down)
        {
            Step(e.Key == Key.Up ? 1 : -1);
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Step(int direction)
    {
        if (Settings.IsReadOnly)
            return;
        var parsed = Options.Parse(_input.Text, typeof(decimal?));
        if (!parsed.IsValid && Value is not null)
        {
            SetError(parsed.Error);
            return;
        }
        try
        {
            decimal candidate = (parsed.Value is decimal current ? current : 0) + direction * Options.Increment;
            if (Options.Minimum is { } min && candidate < min)
                candidate = min;
            if (Options.Maximum is { } max && candidate > max)
                candidate = max;
            Accept(Options.ConvertAndValidate(candidate, ValueType), notifyHost: false);
        }
        catch (OverflowException)
        {
            SetError("The value exceeds the supported numeric range.");
        }
    }
}
