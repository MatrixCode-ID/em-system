using Microsoft.Maui.Graphics;

namespace Em.Ui.Maui.Controls
{
   /// <summary>
   /// One Font Awesome icon, drawn as a letter from a glyph in <see cref="FontIcons"/>. Its size is decided
   /// by <see cref="IconSize"/>, and its color by <see cref="TintColor"/>.
   /// </summary>
   /// <remarks>
   /// Wrapped as a control of its own, not a <c>Label</c> whose font is set at every place of use, so the
   /// name of the icon font only needs to be stated once here - and so its users speak in icon size, not in
   /// letter size.
   /// </remarks>
   public class FontIcon : ContentView
   {
      private readonly Label _label;

      public FontIcon() {
         _label = new Label {
            FontFamily = FontIcons.FontFamily,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
         };
         Content = _label;
         ApplySize();
      }

      /// <summary>The icon glyph character, taken from <see cref="FontIcons"/>.</summary>
      public static readonly BindableProperty GlyphProperty = BindableProperty.Create(
         nameof(Glyph), typeof(string), typeof(FontIcon), string.Empty,
         propertyChanged: (b, _, n) => ((FontIcon)b)._label.Text = (string?)n ?? string.Empty);

      /// <inheritdoc cref="GlyphProperty" />
      public string Glyph {
         get => (string)GetValue(GlyphProperty);
         set => SetValue(GlyphProperty, value);
      }

      /// <summary>Warna ikon.</summary>
      public static readonly BindableProperty TintColorProperty = BindableProperty.Create(
         nameof(TintColor), typeof(Color), typeof(FontIcon), Colors.Black,
         propertyChanged: (b, _, n) => ((FontIcon)b)._label.TextColor = (Color)n);

      /// <inheritdoc cref="TintColorProperty" />
      public Color TintColor {
         get => (Color)GetValue(TintColorProperty);
         set => SetValue(TintColorProperty, value);
      }

      /// <summary>
      /// The side length of the icon in device units. The default is <see cref="FontIcons.DefaultSize"/>.
      /// </summary>
      public static readonly BindableProperty IconSizeProperty = BindableProperty.Create(
         nameof(IconSize), typeof(double), typeof(FontIcon), FontIcons.DefaultSize,
         propertyChanged: (b, _, _) => ((FontIcon)b).ApplySize());

      /// <inheritdoc cref="IconSizeProperty" />
      public double IconSize {
         get => (double)GetValue(IconSizeProperty);
         set => SetValue(IconSizeProperty, value);
      }

      private void ApplySize() {
         // The control's area is made slightly roomier than the icon letter itself: a Font Awesome glyph is drawn
         // inside an em box that leaves space above and below, and a box that fits the letter exactly would cut it
         // off on some devices.
         WidthRequest = HeightRequest = IconSize * 1.25;
         _label.FontSize = IconSize;
      }
   }
}
