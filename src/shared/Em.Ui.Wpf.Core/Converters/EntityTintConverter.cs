using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Em.Shared;
using Color = System.Windows.Media.Color;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Chooses the color of a data row from its name - used for role emblems, module badges, and other rows
   /// that need to be told apart at a glance without their color being stored in the database. The color
   /// slot is decided by <see cref="UiTint"/>, so the same name always gets the same color wherever it
   /// appears - today, tomorrow, and even on another client.
   /// </summary>
   /// <remarks>
   /// Set <c>ConverterParameter</c> to <c>"Text"</c> for the full color (glyph and letters), or leave it
   /// empty for the color of the disc behind it - the same color at 14% opacity. Its tone is chosen to be
   /// medium so it reads well in both light and dark themes.
   /// <para>
   /// A pair with <see cref="AvatarPaletteConverter"/> but not its replacement: that one colors people and
   /// keeps using its own sum of letters, while this one colors entities and uses the shared computation
   /// that the server side also understands.
   /// </para>
   /// </remarks>
   public class EntityTintConverter : IValueConverter
   {
      // Mid-tone hues, readable as a glyph on a light and on a dark background alike. Grey is left
      // out on purpose: a grey tint cannot be told apart from a row that has no tint at all.
      private static readonly Color[] Palette = [
         Color.FromRgb(0x2E, 0x7C, 0xE0),
         Color.FromRgb(0x8B, 0x5C, 0xF6),
         Color.FromRgb(0x0E, 0xA5, 0xA5),
         Color.FromRgb(0xD9, 0x77, 0x06),
         Color.FromRgb(0xDB, 0x27, 0x77),
         Color.FromRgb(0x2F, 0xA8, 0x4F),
      ];

      /// <summary>
      /// Produces a <see cref="SolidColorBrush"/> for the glyph or for the disc behind it, according to
      /// <c>ConverterParameter</c> (see the class remarks).
      /// </summary>
      public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
         var color = Palette[UiTint.SlotOf(value as string, Palette.Length)];

         if (!string.Equals(parameter as string, "Text", StringComparison.OrdinalIgnoreCase))
            color = Color.FromArgb(0x24, color.R, color.G, color.B);

         var brush = new SolidColorBrush(color);
         brush.Freeze();
         return brush;
      }

      /// <summary>
      /// Not supported: the entity color is only computed one way from its name.
      /// </summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("An entity tint cannot be converted back to a name.");
   }
}
