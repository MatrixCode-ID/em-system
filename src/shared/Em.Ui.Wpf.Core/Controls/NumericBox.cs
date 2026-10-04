using System.Globalization;
using System.Windows;
using System.Windows.Input;
using DataObject = System.Windows.DataObject;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Kolom isian bilangan bulat: <see cref="TextBox"/> biasa yang hanya menerima angka, menyimpan
   /// nilainya di <see cref="Value"/>, dan menjaganya tetap di antara <see cref="Minimum"/> dan
   /// <see cref="Maximum"/>. Tombol panah atas/bawah di keyboard menaikkan atau menurunkan nilainya
   /// sebesar <see cref="Increment"/>.
   /// </summary>
   /// <remarks>
   /// Kontrol ini tidak punya tampilan sendiri: tanpa style ia tetap sebuah <see cref="TextBox"/> yang
   /// berfungsi. Style <c>fieldNumericBoxStyle</c> di <c>Styles/Inputs.xaml</c> memberinya bingkai
   /// field dan dua tombol naik/turun, yang menjalankan <see cref="IncreaseCommand"/> dan
   /// <see cref="DecreaseCommand"/>.
   /// <code>
   /// &lt;controls:NumericBox Style="{StaticResource fieldNumericBoxStyle}"
   ///                      Minimum="1" Maximum="600" Value="{Binding Timeout}" /&gt;
   /// </code>
   /// </remarks>
   public class NumericBox : TextBox
   {
      /// <summary>
      /// Menaikkan <see cref="Value"/> sebesar <see cref="Increment"/>. Dipakai tombol naik di
      /// template; target command-nya adalah <see cref="NumericBox"/> tempat tombol itu berada.
      /// </summary>
      public static readonly RoutedCommand IncreaseCommand = new(nameof(IncreaseCommand), typeof(NumericBox));

      /// <summary>
      /// Menurunkan <see cref="Value"/> sebesar <see cref="Increment"/>. Pasangan
      /// <see cref="IncreaseCommand"/>.
      /// </summary>
      public static readonly RoutedCommand DecreaseCommand = new(nameof(DecreaseCommand), typeof(NumericBox));

      /// <summary>Mengidentifikasi property <see cref="Value"/>.</summary>
      public static readonly DependencyProperty ValueProperty =
         DependencyProperty.Register(
            nameof(Value), typeof(int), typeof(NumericBox),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
               OnValueChanged, CoerceValue));

      /// <summary>Mengidentifikasi property <see cref="Minimum"/>.</summary>
      public static readonly DependencyProperty MinimumProperty =
         DependencyProperty.Register(
            nameof(Minimum), typeof(int), typeof(NumericBox),
            new FrameworkPropertyMetadata(int.MinValue, OnRangeChanged));

      /// <summary>Mengidentifikasi property <see cref="Maximum"/>.</summary>
      public static readonly DependencyProperty MaximumProperty =
         DependencyProperty.Register(
            nameof(Maximum), typeof(int), typeof(NumericBox),
            new FrameworkPropertyMetadata(int.MaxValue, OnRangeChanged));

      /// <summary>Mengidentifikasi property <see cref="Increment"/>.</summary>
      public static readonly DependencyProperty IncrementProperty =
         DependencyProperty.Register(
            nameof(Increment), typeof(int), typeof(NumericBox),
            new FrameworkPropertyMetadata(1));

      // Set while the box writes its own Text from Value, so the TextChanged that follows does not
      // try to parse it straight back.
      private bool _syncingText;

      static NumericBox() {
         CommandManager.RegisterClassCommandBinding(typeof(NumericBox),
            new CommandBinding(IncreaseCommand, (s, _) => ((NumericBox)s).Step(+1), CanStep));
         CommandManager.RegisterClassCommandBinding(typeof(NumericBox),
            new CommandBinding(DecreaseCommand, (s, _) => ((NumericBox)s).Step(-1), CanStep));
      }

      /// <summary>
      /// Membuat kolom isian bilangan dengan nilai awal 0.
      /// </summary>
      public NumericBox() {
         DataObject.AddPastingHandler(this, OnPasting);
         SyncText();
      }

      /// <summary>
      /// Nilai yang sedang diisi. Selalu berada di antara <see cref="Minimum"/> dan
      /// <see cref="Maximum"/>; nilai di luar itu dipangkas ke batas terdekat. Binding-nya dua arah
      /// secara bawaan.
      /// </summary>
      public int Value {
         get => (int)GetValue(ValueProperty);
         set => SetValue(ValueProperty, value);
      }

      /// <summary>
      /// Batas bawah <see cref="Value"/>. Tanda minus hanya bisa diketik kalau batas ini di bawah nol.
      /// Bawaannya <see cref="int.MinValue"/>.
      /// </summary>
      public int Minimum {
         get => (int)GetValue(MinimumProperty);
         set => SetValue(MinimumProperty, value);
      }

      /// <summary>
      /// Batas atas <see cref="Value"/>. Bawaannya <see cref="int.MaxValue"/>.
      /// </summary>
      public int Maximum {
         get => (int)GetValue(MaximumProperty);
         set => SetValue(MaximumProperty, value);
      }

      /// <summary>
      /// Besar langkah tombol naik/turun dan panah atas/bawah di keyboard. Bawaannya 1.
      /// </summary>
      public int Increment {
         get => (int)GetValue(IncrementProperty);
         set => SetValue(IncrementProperty, value);
      }

      private static object CoerceValue(DependencyObject d, object baseValue) {
         var box = (NumericBox)d;
         return Math.Clamp((int)baseValue, box.Minimum, Math.Max(box.Minimum, box.Maximum));
      }

      private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
         var box = (NumericBox)d;
         // While the user is typing a valid number the text already says the same thing; rewriting it
         // would move the caret to the start on every keystroke.
         if (!box.TryParse(box.Text, out var typed) || typed != box.Value) box.SyncText();
      }

      private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
         d.CoerceValue(ValueProperty);

      private static void CanStep(object sender, CanExecuteRoutedEventArgs e) =>
         e.CanExecute = ((NumericBox)sender).IsEnabled && !((NumericBox)sender).IsReadOnly;

      private void Step(int direction) {
         // Widened to long so a step past int.MaxValue clamps instead of wrapping round.
         var next = (long)Value + (long)direction * Increment;
         Value = (int)Math.Clamp(next, Minimum, Math.Max(Minimum, Maximum));
         SyncText();
      }

      private void SyncText() {
         _syncingText = true;
         try {
            Text = Value.ToString(CultureInfo.CurrentCulture);
            CaretIndex = Text.Length;
         }
         finally {
            _syncingText = false;
         }
      }

      private bool TryParse(string text, out int value) =>
         int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture, out value);

      private bool AllowsNegative => Minimum < 0;

      // Digits always; a minus sign only as the first character and only when the range goes below
      // zero. Anything else is refused before it reaches the text.
      private bool IsAcceptable(string input, int position) {
         for (var i = 0; i < input.Length; i++) {
            var c = input[i];
            if (char.IsDigit(c)) continue;
            if (c == '-' && AllowsNegative && position + i == 0) continue;
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
         if (e.DataObject.GetData(typeof(string)) is not string pasted || !IsAcceptable(pasted.Trim(), SelectionStart)) {
            e.CancelCommand();
         }
      }

      /// <inheritdoc />
      protected override void OnPreviewKeyDown(KeyEventArgs e) {
         switch (e.Key) {
            case Key.Up:
               Step(+1);
               e.Handled = true;
               break;
            case Key.Down:
               Step(-1);
               e.Handled = true;
               break;
            // Space is not text input in WPF, so PreviewTextInput never sees it.
            case Key.Space:
               e.Handled = true;
               break;
         }

         base.OnPreviewKeyDown(e);
      }

      /// <inheritdoc />
      protected override void OnTextChanged(System.Windows.Controls.TextChangedEventArgs e) {
         base.OnTextChanged(e);
         if (_syncingText) return;

         // Only a number already inside the range is taken while typing: "6" on the way to "60" must
         // not be clamped up to a minimum of 10 under the user's fingers. The rest is settled when
         // the focus leaves.
         if (TryParse(Text, out var typed) && typed >= Minimum && typed <= Maximum) Value = typed;
      }

      /// <inheritdoc />
      protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) {
         base.OnLostKeyboardFocus(e);

         if (TryParse(Text, out var typed)) Value = typed;
         SyncText();
      }
   }
}
