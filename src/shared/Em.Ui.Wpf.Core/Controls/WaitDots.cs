using System.Windows;
using Control = System.Windows.Controls.Control;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// A small wait marker: three dots that take turns growing and shrinking while moving left and right.
   /// Used wherever there is work being waited on but the screen need not be covered - beside a button, in
   /// a toolbar, inside a tab, or in the corner of a card.
   /// </summary>
   /// <remarks>
   /// This control is lookless: its look comes entirely from the default style in
   /// <c>Themes/Generic.xaml</c>. Its size follows the <c>Width</c> and <c>Height</c> given by its user
   /// (default 40 x 12), and the color of its dots follows <c>Foreground</c> (default the theme accent
   /// color). This marker is only visible and does not hold back clicks; to cover the screen while work
   /// runs, use <see cref="WaitOverlay"/>, which itself uses this marker.
   /// <code>
   /// &lt;local:WaitDots IsActive="{Binding InWaiting}" /&gt;
   /// </code>
   /// </remarks>
   public class WaitDots : Control
   {
      static WaitDots() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WaitDots), new FrameworkPropertyMetadata(typeof(WaitDots)));
      }

      /// <summary>Mengidentifikasi property <see cref="IsActive"/>.</summary>
      public static readonly DependencyProperty IsActiveProperty =
         DependencyProperty.Register(
            nameof(IsActive), typeof(bool), typeof(WaitDots),
            new FrameworkPropertyMetadata(true));

      /// <summary>
      /// Turns this marker on and off. Usually bound to <c>NotifyPropertyBase.InWaiting</c> of its screen's
      /// view model.
      /// </summary>
      /// <remarks>
      /// When <c>false</c>, the dots are not drawn and the animation stops, but its place in the layout
      /// remains, so the content around it does not shift every time work starts or finishes. The default is
      /// <c>true</c>, so a marker placed without binding is immediately seen moving.
      /// </remarks>
      public bool IsActive {
         get => (bool)GetValue(IsActiveProperty);
         set => SetValue(IsActiveProperty, value);
      }
   }
}
