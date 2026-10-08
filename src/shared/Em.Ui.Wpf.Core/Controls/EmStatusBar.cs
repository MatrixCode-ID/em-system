using System.Collections;
using System.Windows;
using Control = System.Windows.Controls.Control;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// A thin bar along the bottom edge of a window: one message on the left, then three rows of items: one
   /// next to the message, one pushed to the right, and the system items at the right edge.
   /// </summary>
   /// <remarks>
   /// This control is lookless: its look comes from the default style in <c>Themes/Generic.xaml</c>. An
   /// item may be any object: a string is drawn as text, a <see cref="UIElement"/> as itself (an icon, a
   /// chip, a small button), and any other object through the data template the window finds for its type.
   /// The bar only draws what it is given; it does not decide what to show.
   /// <code>
   /// &lt;controls:EmStatusBar Text="{Binding StatusBar.Text}"
   ///                       LeftItems="{Binding StatusBar.LeftItems}"
   ///                       RightItems="{Binding StatusBar.RightItems}"
   ///                       SystemItems="{Binding StatusBar.SystemItems}" /&gt;
   /// </code>
   /// </remarks>
   public class EmStatusBar : Control
   {
      static EmStatusBar() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EmStatusBar), new FrameworkPropertyMetadata(typeof(EmStatusBar)));
      }

      /// <summary>Identifies the <see cref="Text"/> property.</summary>
      public static readonly DependencyProperty TextProperty =
         DependencyProperty.Register(nameof(Text), typeof(string), typeof(EmStatusBar),
            new FrameworkPropertyMetadata(null));

      /// <summary>Identifies the <see cref="LeftItems"/> property.</summary>
      public static readonly DependencyProperty LeftItemsProperty =
         DependencyProperty.Register(nameof(LeftItems), typeof(IEnumerable), typeof(EmStatusBar),
            new FrameworkPropertyMetadata(null));

      /// <summary>Identifies the <see cref="RightItems"/> property.</summary>
      public static readonly DependencyProperty RightItemsProperty =
         DependencyProperty.Register(nameof(RightItems), typeof(IEnumerable), typeof(EmStatusBar),
            new FrameworkPropertyMetadata(null));

      /// <summary>Identifies the <see cref="SystemItems"/> property.</summary>
      public static readonly DependencyProperty SystemItemsProperty =
         DependencyProperty.Register(nameof(SystemItems), typeof(IEnumerable), typeof(EmStatusBar),
            new FrameworkPropertyMetadata(null));

      /// <summary>
      /// The message at the left edge of the bar, cut with an ellipsis when it does not fit. Empty or
      /// <c>null</c> leaves the place blank.
      /// </summary>
      public string? Text {
         get => (string?)GetValue(TextProperty);
         set => SetValue(TextProperty, value);
      }

      /// <summary>Items drawn right after <see cref="Text"/>, in order.</summary>
      public IEnumerable? LeftItems {
         get => (IEnumerable?)GetValue(LeftItemsProperty);
         set => SetValue(LeftItemsProperty, value);
      }

      /// <summary>Items drawn on the right, just before <see cref="SystemItems"/>, in order.</summary>
      public IEnumerable? RightItems {
         get => (IEnumerable?)GetValue(RightItemsProperty);
         set => SetValue(RightItemsProperty, value);
      }

      /// <summary>
      /// Items drawn against the right edge of the bar, in order. Meant for the fixed slots the engine owns
      /// (such as the product version), so they keep their place whatever the other rows hold.
      /// </summary>
      public IEnumerable? SystemItems {
         get => (IEnumerable?)GetValue(SystemItemsProperty);
         set => SetValue(SystemItemsProperty, value);
      }
   }
}
