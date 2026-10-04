using System.Globalization;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Satu warna tema dalam bentuk ARGB, tidak terikat framework UI mana pun. Tiap client
   /// menerjemahkannya sendiri ke tipe warnanya (WPF, MAUI, atau pustaka kontrol pihak ketiga).
   /// </summary>
   /// <param name="A">Alpha, 0 = transparan penuh, 255 = pekat.</param>
   /// <param name="R">Komponen merah.</param>
   /// <param name="G">Komponen hijau.</param>
   /// <param name="B">Komponen biru.</param>
   public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
   {
      /// <summary>
      /// Membuat warna pekat (alpha 255) dari komponen merah, hijau, dan biru.
      /// </summary>
      public static ThemeColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

      /// <summary>
      /// Membaca warna dari teks heksadesimal <c>#RRGGBB</c> atau <c>#AARRGGBB</c> (tanda <c>#</c>
      /// boleh tidak ditulis) - format yang sama dengan yang dipakai di XAML.
      /// </summary>
      /// <exception cref="FormatException">Teksnya bukan warna heksadesimal 6 atau 8 digit.</exception>
      public static ThemeColor Parse(string hex) {
         ArgumentNullException.ThrowIfNull(hex);

         var digits = hex.StartsWith('#') ? hex[1..] : hex;
         if (digits.Length is not (6 or 8)
             || !uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"'{hex}' is not a #RRGGBB or #AARRGGBB colour.");

         if (digits.Length == 6)
            value |= 0xFF000000;

         return new ThemeColor((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
      }

      /// <summary>
      /// Warna yang sama dengan alpha lain, mis. untuk latar chip yang memakai warna status dengan
      /// kepekatan rendah.
      /// </summary>
      public ThemeColor WithAlpha(byte alpha) => this with { A = alpha };

      /// <summary>
      /// Warna dalam bentuk teks <c>#AARRGGBB</c>, bisa dibaca kembali lewat <see cref="Parse"/>.
      /// </summary>
      public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
   }
}
