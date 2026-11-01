namespace Em.Shared
{
   /// <summary>
   /// An opaque RGB color for a QR code image, kept free of any UI framework so the same options work on
   /// the server, on WPF and on MAUI.
   /// </summary>
   /// <param name="R">Red channel, 0-255.</param>
   /// <param name="G">Green channel, 0-255.</param>
   /// <param name="B">Blue channel, 0-255.</param>
   public readonly record struct QrColor(byte R, byte G, byte B)
   {
      /// <summary>Pure black, the default color of the dark modules.</summary>
      public static QrColor Black => new(0, 0, 0);

      /// <summary>Pure white, the default color of the light modules and the quiet zone.</summary>
      public static QrColor White => new(255, 255, 255);

      /// <summary>
      /// Relative luminance from 0 (black) to 1 (white), per the WCAG definition. Used to tell which of two
      /// colors is the lighter one, so a QR code can always be drawn dark on light.
      /// </summary>
      public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);

      private static double Linear(byte channel) {
         var c = channel / 255.0;
         return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
      }
   }
}
