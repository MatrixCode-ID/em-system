using Microsoft.Maui.Graphics;

namespace Em.Ui.Maui.Controls
{
   /// <summary>
   /// Satu ikon Font Awesome, digambar sebagai huruf dari glyph di <see cref="FontIcons"/>. Ukurannya
   /// ditentukan <see cref="IconSize"/>, dan warnanya <see cref="TintColor"/>.
   /// </summary>
   /// <remarks>
   /// Dibungkus sebagai control sendiri, bukan <c>Label</c> yang font-nya disetel di tiap tempat
   /// pemakaian, supaya nama font ikonnya cukup disebut satu kali di sini - dan supaya pemakainya
   /// bicara dalam ukuran ikon, bukan ukuran huruf.
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

      /// <summary>Karakter glyph ikon, diambil dari <see cref="FontIcons"/>.</summary>
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
      /// Panjang sisi ikon dalam satuan perangkat. Bawaannya <see cref="FontIcons.DefaultSize"/>.
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
         // Bidang control dibuat sedikit lebih lapang dari huruf ikonnya sendiri: glyph Font Awesome
         // digambar di dalam kotak em yang menyisakan ruang di atas dan bawah, dan kotak sepas huruf
         // akan memotongnya di sebagian perangkat.
         WidthRequest = HeightRequest = IconSize * 1.25;
         _label.FontSize = IconSize;
      }
   }
}
