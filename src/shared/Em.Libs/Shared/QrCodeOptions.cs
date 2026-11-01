namespace Em.Shared
{
   /// <summary>
   /// How a QR code image is produced by <see cref="IQrCodeService"/>. Every property has a default, so
   /// <see cref="Default"/> (or passing no options) gives a black-on-white code with error correction M,
   /// 10 pixels per module and a quiet zone.
   /// </summary>
   public sealed record QrCodeOptions
   {
      /// <summary>The smallest accepted <see cref="PixelsPerModule"/>.</summary>
      public const int MinPixelsPerModule = 1;

      /// <summary>The largest accepted <see cref="PixelsPerModule"/>.</summary>
      public const int MaxPixelsPerModule = 100;

      /// <summary>The options used when none are given.</summary>
      public static QrCodeOptions Default { get; } = new();

      /// <summary>Error correction level. Default <see cref="QrErrorCorrection.M"/>.</summary>
      public QrErrorCorrection ErrorCorrection { get; init; } = QrErrorCorrection.M;

      /// <summary>
      /// Width and height, in pixels, of one module (one square of the code). Default 10; accepted range
      /// <see cref="MinPixelsPerModule"/> to <see cref="MaxPixelsPerModule"/>.
      /// </summary>
      public int PixelsPerModule { get; init; } = 10;

      /// <summary>
      /// Whether the light border of 4 modules is drawn around the code. Default <c>true</c>; most scanners
      /// need it unless the surrounding area is already light and empty.
      /// </summary>
      public bool QuietZone { get; init; } = true;

      /// <summary>Color of the dark modules. Default <see cref="QrColor.Black"/>.</summary>
      public QrColor Dark { get; init; } = QrColor.Black;

      /// <summary>Color of the light modules and the quiet zone. Default <see cref="QrColor.White"/>.</summary>
      public QrColor Light { get; init; } = QrColor.White;
   }
}
