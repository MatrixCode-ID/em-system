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
   /// <summary>Label mengambang untuk field Material, tanpa memakai Tag atau menyimpan password.</summary>
   public static class FieldLabel
   {
      /// <summary>Teks label field.</summary>
      public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
         "Text", typeof(string), typeof(FieldLabel), new PropertyMetadata(null, OnTextChanged));
      /// <summary>Latar takik label; bawaannya mengikuti surface card.</summary>
      public static readonly DependencyProperty NotchBackgroundProperty = DependencyProperty.RegisterAttached(
         "NotchBackground", typeof(Brush), typeof(FieldLabel), new PropertyMetadata(null));
      /// <summary>Mengaktifkan animasi label pendek; mengikuti EnableAnimation aplikasi.</summary>
      public static readonly DependencyProperty EnableAnimationProperty = DependencyProperty.RegisterAttached(
         "EnableAnimation", typeof(bool), typeof(FieldLabel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
      /// <summary>Penanda label perlu berada di garis tepi atas.</summary>
      public static readonly DependencyProperty IsFloatingProperty = DependencyProperty.RegisterAttached(
         "IsFloating", typeof(bool), typeof(FieldLabel), new PropertyMetadata(false));
      /// <summary>Membaca teks label.</summary>
      public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);
      /// <summary>Mengatur teks label.</summary>
      public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);
      /// <summary>Membaca latar takik.</summary>
      public static Brush? GetNotchBackground(DependencyObject d) => (Brush?)d.GetValue(NotchBackgroundProperty);
      /// <summary>Mengatur latar takik.</summary>
      public static void SetNotchBackground(DependencyObject d, Brush? value) => d.SetValue(NotchBackgroundProperty, value);
      /// <summary>Membaca pilihan animasi.</summary>
      public static bool GetEnableAnimation(DependencyObject d) => (bool)d.GetValue(EnableAnimationProperty);
      /// <summary>Mengatur pilihan animasi.</summary>
      public static void SetEnableAnimation(DependencyObject d, bool value) => d.SetValue(EnableAnimationProperty, value);
      /// <summary>Membaca posisi label.</summary>
      public static bool GetIsFloating(DependencyObject d) => (bool)d.GetValue(IsFloatingProperty);
      /// <summary>Mengatur posisi label.</summary>
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
