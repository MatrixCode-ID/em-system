using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Chooses the avatar color of one list row from its identity text (e.g. an account name), so each
   /// person always gets the same color every time the list is reloaded, without that color being stored in
   /// the database.
   /// </summary>
   /// <remarks>
   /// Set <c>ConverterParameter</c> to <c>"Text"</c> to get the color of the initial letters (full color),
   /// or leave it empty/anything else to get the color of the avatar disc background (the same color but
   /// very transparent). The colors are chosen with a medium tone so they stay readable in both light and
   /// dark themes.
   /// </remarks>
   public class AvatarPaletteConverter : IValueConverter
   {
      // Mid-tone hues: readable as text on a light and on a dark background alike.
      private static readonly Color[] Palette = [
         Color.FromRgb(0x2F, 0xA8, 0x4F),
         Color.FromRgb(0x3B, 0x82, 0xF6),
         Color.FromRgb(0xA8, 0x55, 0xF7),
         Color.FromRgb(0xF9, 0x73, 0x16),
         Color.FromRgb(0x14, 0xB8, 0xA6),
         Color.FromRgb(0xE0, 0x56, 0x56),
         Color.FromRgb(0xC9, 0x8A, 0x00),
      ];

      /// <summary>
      /// Produces a <see cref="SolidColorBrush"/> for the initial letters or the avatar background, according
      /// to <c>ConverterParameter</c> (see the class remarks).
      /// </summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
         var color = Palette[PaletteIndexOf(value as string)];

         // The disc is the same hue at a fifth of the opacity, so text and disc always agree.
         if (!string.Equals(parameter as string, "Text", StringComparison.OrdinalIgnoreCase))
            color = Color.FromArgb(0x33, color.R, color.G, color.B);

         var brush = new SolidColorBrush(color);
         brush.Freeze();
         return brush;
      }

      /// <summary>
      /// Not supported: the avatar color is only computed one way from the identity text.
      /// </summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("An avatar colour cannot be converted back to a name.");

      // A hand rolled sum rather than string.GetHashCode: the latter is randomised per process,
      // which would hand the same person a different colour on every run.
      private static int PaletteIndexOf(string? key) {
         if (string.IsNullOrEmpty(key)) return 0;

         var sum = 0;
         foreach (var c in key)
            sum = (sum + c) % Palette.Length;

         return sum;
      }
   }
}
