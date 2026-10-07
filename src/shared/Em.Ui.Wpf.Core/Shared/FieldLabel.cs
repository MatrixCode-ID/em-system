using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Em.Ui.Wpf.Core;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>A floating label for a Material field, without using Tag or storing the password.</summary>
   public static class FieldLabel
   {
      /// <summary>Teks label field.</summary>
      public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
         "Text", typeof(string), typeof(FieldLabel), new PropertyMetadata(null, OnTextChanged));
      /// <summary>The background of the label notch; by default it follows the card surface.</summary>
      public static readonly DependencyProperty NotchBackgroundProperty = DependencyProperty.RegisterAttached(
         "NotchBackground", typeof(Brush), typeof(FieldLabel), new PropertyMetadata(null));
      /// <summary>Turns on the short label animation; follows the application's EnableAnimation.</summary>
      public static readonly DependencyProperty EnableAnimationProperty = DependencyProperty.RegisterAttached(
         "EnableAnimation", typeof(bool), typeof(FieldLabel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
      /// <summary>Marker that the label needs to sit on the top border line.</summary>
      public static readonly DependencyProperty IsFloatingProperty = DependencyProperty.RegisterAttached(
         "IsFloating", typeof(bool), typeof(FieldLabel), new PropertyMetadata(false));
      /// <summary>Reads the label text.</summary>
      public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
      /// <summary>Mengatur teks label.</summary>
      public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);
      /// <summary>Reads the notch background.</summary>
      public static Brush? GetNotchBackground(DependencyObject d) => (Brush?)d.GetValue(NotchBackgroundProperty);
      /// <summary>Mengatur latar takik.</summary>
      public static void SetNotchBackground(DependencyObject d, Brush? value) => d.SetValue(NotchBackgroundProperty, value);
      /// <summary>Reads the animation choice.</summary>
      public static bool GetEnableAnimation(DependencyObject d) => (bool)d.GetValue(EnableAnimationProperty);
      /// <summary>Sets the animation choice.</summary>
      public static void SetEnableAnimation(DependencyObject d, bool value) => d.SetValue(EnableAnimationProperty, value);
      /// <summary>Reads the label position.</summary>
      public static bool GetIsFloating(DependencyObject d) => (bool)d.GetValue(IsFloatingProperty);
      /// <summary>Sets the label position.</summary>
      public static void SetIsFloating(DependencyObject d, bool value) => d.SetValue(IsFloatingProperty, value);

      private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
         if (d is not Control c) return;
         if (e.OldValue is null && e.NewValue is not null) {
            c.Loaded += Loaded;
            c.Unloaded += Unloaded;
            if (c.IsLoaded) Attach(c);
         }
         else if (e.NewValue is null) {
            c.Loaded -= Loaded;
            c.Unloaded -= Unloaded;
            Detach(c);
         }
      }
      private static void Loaded(object sender, RoutedEventArgs e) => Attach((Control)sender);
      private static void Unloaded(object sender, RoutedEventArgs e) => Detach((Control)sender);
      private static void Attach(Control c) {
         Detach(c);
         c.IsKeyboardFocusWithinChanged += FocusChanged;
         if (c is TextBox text) text.TextChanged += Changed;
         if (c is PasswordBox password) password.PasswordChanged += Changed;
         if (c is ComboBox combo) combo.SelectionChanged += Changed;
         Update(c, false);
      }
      private static void Detach(Control c) {
         c.IsKeyboardFocusWithinChanged -= FocusChanged;
         if (c is TextBox text) text.TextChanged -= Changed;
         if (c is PasswordBox password) password.PasswordChanged -= Changed;
         if (c is ComboBox combo) combo.SelectionChanged -= Changed;
      }
      private static void Changed(object sender, RoutedEventArgs e) => Update((Control)sender, true);
      private static void FocusChanged(object sender, DependencyPropertyChangedEventArgs e) => Update((Control)sender, true);
      private static void Update(Control c, bool animate) {
         var filled = c switch {
            TextBox text => text.Text.Length > 0,
            PasswordBox password => password.Password.Length > 0,
            ComboBox combo => combo.SelectedItem is not null || !string.IsNullOrEmpty(combo.Text),
            _ => false
         };
         var floating = filled || c.IsKeyboardFocusWithin;
         SetIsFloating(c, floating);
         c.ApplyTemplate();
         if (c.Template.FindName("labelNotch", c) is FrameworkElement label
            && label.RenderTransform is TranslateTransform transform) {
            if (transform.IsFrozen) {
               transform = transform.Clone();
               label.RenderTransform = transform;
            }
            var target = floating ? -8d : 18d;
            var from = transform.Y;
            transform.BeginAnimation(TranslateTransform.YProperty, null);
            transform.Y = target;
            if (animate && GetEnableAnimation(c))
               transform.BeginAnimation(TranslateTransform.YProperty,
                  new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop });
         }
      }
   }
}
