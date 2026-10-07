using System.Globalization;

namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// One theme color in ARGB form, not tied to any UI framework. Each client translates it to its own
   /// color type (WPF, MAUI, or a third-party control library).
   /// </summary>
   /// <param name="A">Alpha, 0 = fully transparent, 255 = opaque.</param>
   /// <param name="R">The red component.</param>
   /// <param name="G">The green component.</param>
   /// <param name="B">The blue component.</param>
   public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
   {
      /// <summary>
      /// Creates an opaque color (alpha 255) from red, green, and blue components.
      /// </summary>
      public static ThemeColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

      /// <summary>
      /// Reads a color from hexadecimal text <c>#RRGGBB</c> or <c>#AARRGGBB</c> (the <c>#</c> sign may be
      /// omitted) - the same format as used in XAML.
      /// </summary>
      /// <exception cref="FormatException">The text is not a 6 or 8 digit hexadecimal color.</exception>
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
      /// The same color with another alpha, e.g. for the background of a chip that uses the status color at
      /// low opacity.
      /// </summary>
      public ThemeColor WithAlpha(byte alpha) => this with { A = alpha };

      /// <summary>
      /// The color as <c>#AARRGGBB</c> text, which can be read back through <see cref="Parse"/>.
      /// </summary>
      public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
   }
}
