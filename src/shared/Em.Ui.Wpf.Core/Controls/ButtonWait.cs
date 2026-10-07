using System.Windows;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Makes a button show by itself that its work is being waited on: while
   /// <see cref="IsWaitingProperty"/> is <c>true</c>, the text or icon inside the button is replaced by
   /// small wait dots (<see cref="WaitDots"/>) and clicks on that button are held back. The button width
   /// does not change, so other buttons beside it do not shift.
   /// </summary>
   /// <remarks>
   /// Applies to all bordered buttons from <c>Styles/Buttons.xaml</c> (filled, tonal, outlined, danger,
   /// compact). The dots use the theme accent color, the same as the main wait marker; only on a filled
   /// button (whose background is already the accent) are the dots white. The button can still be pressed
   /// by keyboard while waiting; if the work must not run twice, guard its command too.
   /// <code>
   /// &lt;Button Content="Refresh" Style="{StaticResource tonalButtonStyle}"
   ///         local:ButtonWait.IsWaiting="{Binding IsRefreshing}"
   ///         Command="{Binding Commands[RefreshCommand]}" /&gt;
   /// </code>
   /// </remarks>
   public static class ButtonWait
   {
      /// <summary>Mengidentifikasi attached property <c>IsWaiting</c>.</summary>
      public static readonly DependencyProperty IsWaitingProperty =
         DependencyProperty.RegisterAttached(
            "IsWaiting", typeof(bool), typeof(ButtonWait),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi attached property <c>OnAccent</c>.</summary>
      public static readonly DependencyProperty OnAccentProperty =
         DependencyProperty.RegisterAttached(
            "OnAccent", typeof(bool), typeof(ButtonWait),
            new FrameworkPropertyMetadata(false));

      /// <summary>
      /// Reads whether the button has an accent-colored background, so its dots must be white. Filled by the
      /// filled button style; other buttons use the same accent color as the main wait marker.
      /// </summary>
      public static bool GetOnAccent(DependencyObject element) =>
         (bool)element.GetValue(OnAccentProperty);

      /// <summary>Marks the button as having an accent-colored background.</summary>
      public static void SetOnAccent(DependencyObject element, bool value) =>
         element.SetValue(OnAccentProperty, value);

      /// <summary>Reads whether the button is showing the wait dots.</summary>
      public static bool GetIsWaiting(DependencyObject element) =>
         (bool)element.GetValue(IsWaitingProperty);

      /// <summary>Turns the wait dots inside the button on or off.</summary>
      public static void SetIsWaiting(DependencyObject element, bool value) =>
         element.SetValue(IsWaitingProperty, value);
   }
}
