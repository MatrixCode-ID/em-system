using System.Windows;
using Control = System.Windows.Controls.Control;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// A wait layer that covers the screen while there is work to wait for - loading a list, saving a row,
   /// or anything waiting for the server's answer. Besides telling that the application is working, it also
   /// holds back clicks, so the same work cannot be run twice just because its button was pressed again.
   /// </summary>
   /// <remarks>
   /// This control is lookless: its look comes entirely from the default style in
   /// <c>Themes/Generic.xaml</c>, so its user only needs to put it as the last child of the panel to be
   /// covered and bind <see cref="IsWaiting"/>. Putting it last matters: what is drawn last is on top.
   /// <code>
   /// &lt;local:WaitOverlay IsWaiting="{Binding InWaiting}" Caption="{Binding WaiterText}" /&gt;
   /// </code>
   /// This layer only covers the panel where it is placed, so one screen may have several layers at the
   /// same time - for example one per grid - each bound to its own property (<c>LeftGridWaiting</c>,
   /// <c>RightGridWaiting</c>, ...) with its own <see cref="Heading"/>, <see cref="Caption"/> and
   /// <c>Background</c>. A small area automatically shows only the wait dots (see
   /// <see cref="IsCompact"/>). The view model's built-in <c>InWaiting</c> is enough for one layer; for
   /// several layers create a bool property per area.
   /// </remarks>
   public class WaitOverlay : Control
   {
      static WaitOverlay() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WaitOverlay), new FrameworkPropertyMetadata(typeof(WaitOverlay)));
      }

      /// <summary>Mengidentifikasi property <see cref="IsBare"/>.</summary>
      public static readonly DependencyProperty IsBareProperty =
         DependencyProperty.Register(
            nameof(IsBare), typeof(bool), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi property <see cref="CornerRadius"/>.</summary>
      public static readonly DependencyProperty CornerRadiusProperty =
         DependencyProperty.Register(
            nameof(CornerRadius), typeof(CornerRadius), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(new CornerRadius(0)));

      /// <summary>
      /// When <c>true</c>, the layer only shows the large wait dots directly on the scrim: without the card,
      /// <see cref="Heading"/>, and <see cref="Caption"/>. Suitable for a grid or list that only needs to be
      /// dimmed while its data is reloaded.
      /// </summary>
      public bool IsBare {
         get => (bool)GetValue(IsBareProperty);
         set => SetValue(IsBareProperty, value);
      }

      /// <summary>
      /// The corner curvature of the scrim. Match it to the <c>CornerRadius</c> of the panel being covered so
      /// the scrim corners do not stick out of a panel with rounded corners.
      /// </summary>
      public CornerRadius CornerRadius {
         get => (CornerRadius)GetValue(CornerRadiusProperty);
         set => SetValue(CornerRadiusProperty, value);
      }

      private static readonly DependencyPropertyKey IsCompactPropertyKey =
         DependencyProperty.RegisterReadOnly(
            nameof(IsCompact), typeof(bool), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi property <see cref="IsCompact"/>.</summary>
      public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;

      /// <summary>
      /// <c>true</c> while the area being covered is too small to hold the full card (narrower than 220 or
      /// shorter than 140). The card then only shows the wait dots, without <see cref="Heading"/> and
      /// <see cref="Caption"/>. Recomputed every time the area's size changes, so the same layer can be used
      /// on both big and small grids.
      /// </summary>
      public bool IsCompact => (bool)GetValue(IsCompactProperty);

      /// <inheritdoc />
      protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) {
         base.OnRenderSizeChanged(sizeInfo);
         SetValue(IsCompactPropertyKey, sizeInfo.NewSize.Width < 220 || sizeInfo.NewSize.Height < 140);
      }

      /// <summary>Mengidentifikasi property <see cref="IsWaiting"/>.</summary>
      public static readonly DependencyProperty IsWaitingProperty =
         DependencyProperty.Register(
            nameof(IsWaiting), typeof(bool), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(false));

      /// <summary>Mengidentifikasi property <see cref="Heading"/>.</summary>
      public static readonly DependencyProperty HeadingProperty =
         DependencyProperty.Register(
            nameof(Heading), typeof(string), typeof(WaitOverlay),
            new FrameworkPropertyMetadata("Please wait"));

      /// <summary>Mengidentifikasi property <see cref="Caption"/>.</summary>
      public static readonly DependencyProperty CaptionProperty =
         DependencyProperty.Register(
            nameof(Caption), typeof(string), typeof(WaitOverlay),
            new FrameworkPropertyMetadata(default(string)));

      /// <summary>
      /// Turns this layer on and off. Usually bound to <c>NotifyPropertyBase.InWaiting</c> of its screen's
      /// view model.
      /// </summary>
      /// <remarks>
      /// The layer starts holding back clicks as soon as it is <c>true</c>, but only becomes visible after a
      /// short delay. So work that finishes in an instant leaves no flicker of the wait marker on screen,
      /// while a second click is still held back from the first moment.
      /// </remarks>
      public bool IsWaiting {
         get => (bool)GetValue(IsWaitingProperty);
         set => SetValue(IsWaitingProperty, value);
      }

      /// <summary>
      /// The first line below the wait dots. Its content stays the same for as long as the screen lives, so
      /// use a sentence that applies to all work on that screen.
      /// </summary>
      public string Heading {
         get => (string)GetValue(HeadingProperty);
         set => SetValue(HeadingProperty, value);
      }

      /// <summary>
      /// The second line, which names the work running right now - usually bound to
      /// <c>NotifyPropertyBase.WaiterText</c>. When empty, the line is not drawn at all.
      /// </summary>
      public string? Caption {
         get => (string?)GetValue(CaptionProperty);
         set => SetValue(CaptionProperty, value);
      }
   }
}
