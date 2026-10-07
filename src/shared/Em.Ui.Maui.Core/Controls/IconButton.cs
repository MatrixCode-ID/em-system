using System.Windows.Input;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace Em.Ui.Maui.Controls
{
   /// <summary>
   /// A Material-style round icon button: an icon inside a 48x48 touch area, with a brief flash to show it
   /// was really pressed. Used in the app bar and other places whose action is well represented by a single
   /// picture.
   /// </summary>
   /// <remarks>
   /// Written as a control of its own, not a <c>Button</c> with an image, because a <c>Button</c> in MAUI
   /// only accepts an <c>ImageSource</c> - while the icon here is drawn as a font letter so its color can
   /// follow the theme without preparing one image file per color.
   /// </remarks>
   public class IconButton : ContentView
   {
      // The state layer while the pointer is over the button. A translucent gray, not a particular color: it
      // darkens the light theme and brightens the dark theme, so a single value is enough for both - the same
      // rule used by the whole design language in this repo.
      private static readonly Color HoverColor = Color.FromRgba(128, 128, 128, 31);

      private readonly FontIcon _icon;
      private readonly Border _surface;
      private readonly RoundRectangle _surfaceShape;

      public IconButton() {
         _icon = new FontIcon {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
         };
         _surfaceShape = new RoundRectangle { CornerRadius = 24 };
         _surface = new Border {
            Padding = 0,
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            StrokeShape = _surfaceShape,
            Content = _icon
         };
         Content = _surface;
         WidthRequest = HeightRequest = 48;

         // The area is always fully round, whatever the button size - and the size is indeed changed at several
         // places of use, so the radius is recomputed rather than set once.
         SizeChanged += (_, _) => _surfaceShape.CornerRadius = Math.Min(Width, Height) / 2;

         var tap = new TapGestureRecognizer();
         tap.Tapped += OnTapped;
         GestureRecognizers.Add(tap);

         // Hover only exists on devices that have a pointer - a mouse on desktop, or a stylus. On a touch screen
         // these two events never arrive, and what gives the feedback there is the flash in OnTapped. Both are
         // attached together so this control need not know which device it is running on.
         var pointer = new PointerGestureRecognizer();
         pointer.PointerEntered += (_, _) => _surface.BackgroundColor = HoverColor;
         pointer.PointerExited += (_, _) => _surface.BackgroundColor = Colors.Transparent;
         GestureRecognizers.Add(pointer);
      }

      /// <summary>The icon glyph character, taken from <see cref="FontIcons"/>.</summary>
      public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
         nameof(Glyph), typeof(string), typeof(IconButton), string.Empty,
         propertyChanged: (b, _, n) => ((IconButton)b)._icon.Glyph = (string)n);

      /// <inheritdoc cref="GlyphProperty" />
      public string Glyph {
         get => (string)GetValue(GlyphProperty);
         set => SetValue(GlyphProperty, value);
      }

      /// <summary>Warna ikon.</summary>
      public static readonly BindableProperty TintColorProperty = BindableProperty.Create(
         nameof(TintColor), typeof(Color), typeof(IconButton), Colors.Black,
         propertyChanged: (b, _, n) => ((IconButton)b)._icon.TintColor = (Color)n);

      /// <inheritdoc cref="TintColorProperty" />
      public Color TintColor {
         get => (Color)GetValue(TintColorProperty);
         set => SetValue(TintColorProperty, value);
      }

      /// <summary>The side length of the icon itself, not its touch area. The default is 24.</summary>
      public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
         nameof(IconSize), typeof(double), typeof(IconButton), FontIcons.DefaultSize,
         propertyChanged: (b, _, n) => ((IconButton)b)._icon.IconSize = (double)n);

      /// <inheritdoc cref="IconSizeProperty" />
      public double IconSize {
         get => (double)GetValue(IconSizeProperty);
         set => SetValue(IconSizeProperty, value);
      }

      /// <summary>The command that runs when the button is pressed.</summary>
      public static readonly BindableProperty CommandProperty = BindableProperty.Create(
         nameof(Command), typeof(ICommand), typeof(IconButton));

      /// <inheritdoc cref="CommandProperty" />
      public ICommand? Command {
         get => (ICommand?)GetValue(CommandProperty);
         set => SetValue(CommandProperty, value);
      }

      /// <summary>The parameter passed to <see cref="Command"/>.</summary>
      public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
         nameof(CommandParameter), typeof(object), typeof(IconButton));

      /// <inheritdoc cref="CommandParameterProperty" />
      public object? CommandParameter {
         get => GetValue(CommandParameterProperty);
         set => SetValue(CommandParameterProperty, value);
      }

      private async void OnTapped(object? sender, TappedEventArgs e) {
         if (Command is not { } command || !command.CanExecute(CommandParameter)) return;

         // A brief flash in place of the platform's default ripple effect, which is not available for a control
         // that draws itself like this one. Deliberately run to completion before the command runs: a screen
         // change replaces the whole page content, and an animation still running on a disposed control never
         // finishes.
         await this.FadeToAsync(0.4, 60);
         await this.FadeToAsync(1, 90);
         command.Execute(CommandParameter);
      }
   }
}
