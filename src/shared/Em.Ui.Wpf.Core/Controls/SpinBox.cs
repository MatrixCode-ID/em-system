using System.Globalization;
using System.Windows;
using System.Windows.Input;
using DataObject = System.Windows.DataObject;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// A number field with spin buttons, for values that may have decimals or may stay empty. It is an ordinary
   /// <see cref="TextBox"/> that only accepts numbers and keeps its value in <see cref="Value"/>, between
   /// <see cref="Minimum"/> and <see cref="Maximum"/>. The up and down arrow keys and the two buttons move it by
   /// <see cref="Step"/>. Use <see cref="NumericBox"/> for a whole number that is always set.
   /// </summary>
   /// <remarks>
   /// The <c>fieldSpinBoxStyle</c> style in <c>Styles/Inputs.xaml</c> gives it the field border and the buttons.
   /// Numbers are read and written in en-US format.
   /// <code>
   /// &lt;controls:SpinBox Style="{StaticResource fieldSpinBoxStyle}" Minimum="0" Maximum="24" Step="0.5"
   ///                   DecimalPlaces="2" Value="{Binding Hours}" /&gt;
   /// </code>
   /// </remarks>
   public class SpinBox : TextBox
   {
      /// <summary>Raises <see cref="Value"/> by <see cref="Step"/>.</summary>
      public static readonly RoutedCommand IncreaseCommand = new(nameof(IncreaseCommand), typeof(SpinBox));

      /// <summary>Lowers <see cref="Value"/> by <see cref="Step"/>.</summary>
      public static readonly RoutedCommand DecreaseCommand = new(nameof(DecreaseCommand), typeof(SpinBox));

      /// <summary>Identifies the <see cref="Value"/> property.</summary>
      public static readonly DependencyProperty ValueProperty =
         DependencyProperty.Register(nameof(Value), typeof(decimal?), typeof(SpinBox),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

      /// <summary>Identifies the <see cref="Minimum"/> property.</summary>
      public static readonly DependencyProperty MinimumProperty =
         DependencyProperty.Register(nameof(Minimum), typeof(decimal), typeof(SpinBox), new FrameworkPropertyMetadata(0m, OnRangeChanged));

      /// <summary>Identifies the <see cref="Maximum"/> property.</summary>
      public static readonly DependencyProperty MaximumProperty =
         DependencyProperty.Register(nameof(Maximum), typeof(decimal), typeof(SpinBox), new FrameworkPropertyMetadata(decimal.MaxValue, OnRangeChanged));

      /// <summary>Identifies the <see cref="Step"/> property.</summary>
      public static readonly DependencyProperty StepProperty =
         DependencyProperty.Register(nameof(Step), typeof(decimal), typeof(SpinBox), new FrameworkPropertyMetadata(1m));

      /// <summary>Identifies the <see cref="DecimalPlaces"/> property.</summary>
      public static readonly DependencyProperty DecimalPlacesProperty =
         DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(SpinBox), new FrameworkPropertyMetadata(0, OnRangeChanged));

      /// <summary>Identifies the <see cref="AllowEmpty"/> property.</summary>
      public static readonly DependencyProperty AllowEmptyProperty =
         DependencyProperty.Register(nameof(AllowEmpty), typeof(bool), typeof(SpinBox), new FrameworkPropertyMetadata(false, OnRangeChanged));

      private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

      // Set while the box writes its own Text from Value, so the TextChanged that follows does not parse it back.
      private bool _syncingText;

      static SpinBox() {
         CommandManager.RegisterClassCommandBinding(typeof(SpinBox), new CommandBinding(IncreaseCommand, (s, _) => ((SpinBox)s).Spin(+1), CanSpin));
         CommandManager.RegisterClassCommandBinding(typeof(SpinBox), new CommandBinding(DecreaseCommand, (s, _) => ((SpinBox)s).Spin(-1), CanSpin));
      }

      /// <summary>Creates an empty number field.</summary>
      public SpinBox() {
         DataObject.AddPastingHandler(this, OnPasting);
         SyncText();
      }

      /// <summary>The number, between the limits and rounded to <see cref="DecimalPlaces"/>. Empty is <c>null</c>. Binds two-way by default.</summary>
      public decimal? Value {
         get => (decimal?)GetValue(ValueProperty);
         set => SetValue(ValueProperty, value);
      }

      /// <summary>The lower limit. A minus sign can only be typed when it is below zero. The default is 0.</summary>
      public decimal Minimum {
         get => (decimal)GetValue(MinimumProperty);
         set => SetValue(MinimumProperty, value);
      }

      /// <summary>The upper limit. The default is the largest decimal.</summary>
      public decimal Maximum {
         get => (decimal)GetValue(MaximumProperty);
         set => SetValue(MaximumProperty, value);
      }

      /// <summary>How far the buttons and arrow keys move the value. The default is 1.</summary>
      public decimal Step {
         get => (decimal)GetValue(StepProperty);
         set => SetValue(StepProperty, value);
      }

      /// <summary>How many decimals are kept. The default is 0, a whole number.</summary>
      public int DecimalPlaces {
         get => (int)GetValue(DecimalPlacesProperty);
         set => SetValue(DecimalPlacesProperty, value);
      }

      /// <summary>Whether the field may stay empty. The default is false, where an empty field goes back to its last value.</summary>
      public bool AllowEmpty {
         get => (bool)GetValue(AllowEmptyProperty);
         set => SetValue(AllowEmptyProperty, value);
      }

      private static object? CoerceValue(DependencyObject d, object? baseValue) {
         var box = (SpinBox)d;
         return baseValue is decimal value ? box.Fit(value) : baseValue;
      }

      private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
         var box = (SpinBox)d;
         // While a valid number is being typed the text already says the same thing; rewriting it would move the caret.
         if (box.Value is null ? box.Text.Length > 0 : !box.TryParse(box.Text, out var typed) || typed != box.Value) box.SyncText();
      }

      private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => d.CoerceValue(ValueProperty);

      private static void CanSpin(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = ((SpinBox)sender).IsEnabled && !((SpinBox)sender).IsReadOnly;

      private decimal Fit(decimal value) => Math.Clamp(Math.Round(value, Math.Clamp(DecimalPlaces, 0, 28), MidpointRounding.AwayFromZero), Minimum, Math.Max(Minimum, Maximum));

      private void Spin(int direction) {
         // An empty field starts from its lower limit; the limits keep the sum inside decimal.
         var current = Value ?? (direction > 0 ? Minimum - Step : Minimum + Step);
         var limit = direction > 0 ? Math.Max(Minimum, Maximum) - current : current - Minimum;
         Value = Fit(limit < Step ? (direction > 0 ? Math.Max(Minimum, Maximum) : Minimum) : current + direction * Step);
         SyncText();
      }

      private string Format(decimal value) => value.ToString(DecimalPlaces > 0 ? "0." + new string('#', DecimalPlaces) : "0", Culture);

      private void SyncText() {
         _syncingText = true;
         try {
            Text = Value is { } value ? Format(value) : "";
            CaretIndex = Text.Length;
         }
         finally { _syncingText = false; }
      }

      private bool TryParse(string text, out decimal value) => decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, Culture, out value);

      // Digits; one decimal point when decimals are kept; a minus sign only first and only when the range goes below zero.
      private bool IsAcceptable(string input, int position) {
         var separator = Culture.NumberFormat.NumberDecimalSeparator;
         var hasSeparator = Text.Remove(SelectionStart, SelectionLength).Contains(separator);
         for (var i = 0; i < input.Length; i++) {
            var c = input[i];
            if (char.IsDigit(c)) continue;
            if (c == '-' && Minimum < 0 && position + i == 0) continue;
            if (DecimalPlaces > 0 && separator.Contains(c) && !hasSeparator) { hasSeparator = true; continue; }
            return false;
         }
         return true;
      }

      /// <inheritdoc />
      protected override void OnPreviewTextInput(TextCompositionEventArgs e) {
         if (!IsAcceptable(e.Text, SelectionStart)) e.Handled = true;
         base.OnPreviewTextInput(e);
      }

      private void OnPasting(object sender, DataObjectPastingEventArgs e) {
         if (e.DataObject.GetData(typeof(string)) is not string pasted || !IsAcceptable(pasted.Trim(), SelectionStart)) e.CancelCommand();
      }

      /// <inheritdoc />
      protected override void OnPreviewKeyDown(KeyEventArgs e) {
         switch (e.Key) {
            case Key.Up: Spin(+1); e.Handled = true; break;
            case Key.Down: Spin(-1); e.Handled = true; break;
            // Space is not text input in WPF, so PreviewTextInput never sees it.
            case Key.Space: e.Handled = true; break;
         }
         base.OnPreviewKeyDown(e);
      }

      /// <inheritdoc />
      protected override void OnTextChanged(System.Windows.Controls.TextChangedEventArgs e) {
         base.OnTextChanged(e);
         if (_syncingText) return;
         // Only a number already inside the range is taken while typing; the rest is settled when the focus leaves.
         if (Text.Length == 0) { if (AllowEmpty) Value = null; }
         else if (TryParse(Text, out var typed) && typed == Fit(typed)) Value = typed;
      }

      /// <inheritdoc />
      protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) {
         base.OnLostKeyboardFocus(e);
         if (TryParse(Text, out var typed)) Value = Fit(typed);
         SyncText();
      }
   }
}
