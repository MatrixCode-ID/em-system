namespace Em.Shared
{
   /// <summary>
   /// Turns a text into a QR code image. The engine registers it on the server and on every UI host, so a
   /// module only resolves it from the service provider; no host code is needed.
   /// </summary>
   /// <remarks>
   /// The UI cores add helpers on top of it that return an image source, plus a <c>QrCodeView</c> control
   /// that follows the theme. See <c>doc/engine/engine-qr-code.md</c>.
   /// </remarks>
   public interface IQrCodeService
   {
      /// <summary>Encodes <paramref name="text"/> as a QR code and returns it as a PNG image.</summary>
      /// <param name="text">
      /// The text to encode. Digits and upper-case alphanumeric text are encoded compactly; text outside
      /// ISO-8859-1 is encoded as UTF-8 with an ECI marker so scanners read it back unchanged.
      /// </param>
      /// <param name="options">How the image is drawn; <c>null</c> means <see cref="QrCodeOptions.Default"/>.</param>
      /// <returns>The PNG bytes, ready to be saved or shown.</returns>
      /// <exception cref="ArgumentNullException"><paramref name="text"/> is <c>null</c>.</exception>
      /// <exception cref="ArgumentException">
      /// <paramref name="text"/> is empty, or too long to fit a QR code at the chosen error correction level.
      /// </exception>
      /// <exception cref="ArgumentOutOfRangeException">
      /// <see cref="QrCodeOptions.PixelsPerModule"/> is outside the accepted range.
      /// </exception>
      byte[] CreatePng(string text, QrCodeOptions? options = null);
   }
}
