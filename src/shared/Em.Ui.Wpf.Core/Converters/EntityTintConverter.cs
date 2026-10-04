using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Em.Shared;
using Color = System.Windows.Media.Color;

namespace Em.Ui.Wpf.Converters
{
   /// <summary>
   /// Memilih warna sebuah baris data dari namanya - dipakai emblem role, lencana module, dan baris
   /// lain yang perlu dibedakan sekilas tanpa warnanya ikut disimpan di database. Slot warnanya
   /// ditentukan <see cref="UiTint"/>, sehingga nama yang sama selalu mendapat warna yang sama di
   /// mana pun ia muncul - hari ini, besok, dan di klien lain sekali pun.
   /// </summary>
   /// <remarks>
   /// Isi <c>ConverterParameter</c> dengan <c>"Text"</c> untuk warna penuh (glyph dan huruf), atau
   /// kosongkan untuk warna cakram di belakangnya - warna yang sama pada kepekatan 14%. Nadanya
   /// dipilih sedang supaya terbaca baik di tema terang maupun gelap.
   /// <para>
   /// Sepasang dengan <see cref="AvatarPaletteConverter"/> tapi bukan penggantinya: yang itu
   /// mewarnai orang dan tetap memakai penjumlahan hurufnya sendiri, yang ini mewarnai entitas dan
   /// memakai perhitungan bersama yang juga dipahami sisi server.
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
      /// Menghasilkan <see cref="SolidColorBrush"/> untuk glyph atau untuk cakram di belakangnya,
      /// sesuai <c>ConverterParameter</c> (lihat keterangan kelas).
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
      /// Tidak didukung: warna entitas hanya dihitung satu arah dari namanya.
      /// </summary>
      public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
         throw new NotSupportedException("An entity tint cannot be converted back to a name.");
   }
}
