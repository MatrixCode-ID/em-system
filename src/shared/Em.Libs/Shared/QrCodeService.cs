using QRCoder;
using QRCoder.Exceptions;

namespace Em.Shared
{
   /// <summary>
   /// The engine's <see cref="IQrCodeService"/>, built on QRCoder's managed PNG renderer (no
   /// <c>System.Drawing</c> or native graphics library), so it runs the same on the server, Windows and
   /// Android. It holds no state and is safe to use from several threads at once.
   /// </summary>
   public sealed class QrCodeService : IQrCodeService
   {
      /// <summary>
      /// The instance the engine registers, also used by the UI <c>QrCodeView</c> controls. Creating another
      /// instance is allowed and behaves the same.
      /// </summary>
      public static QrCodeService Default { get; } = new();

      /// <inheritdoc />
      public byte[] CreatePng(string text, QrCodeOptions? options = null) {
         ArgumentNullException.ThrowIfNull(text);
         if (text.Length == 0)
            throw new ArgumentException("The text to encode as a QR code is empty.", nameof(text));
         options ??= QrCodeOptions.Default;
         if (options.PixelsPerModule is < QrCodeOptions.MinPixelsPerModule or > QrCodeOptions.MaxPixelsPerModule)
            throw new ArgumentOutOfRangeException(nameof(options), options.PixelsPerModule,
               $"PixelsPerModule must be between {QrCodeOptions.MinPixelsPerModule} and {QrCodeOptions.MaxPixelsPerModule}.");

         // QRCoder picks numeric/alphanumeric/byte mode by itself; only text that ISO-8859-1 cannot hold
         // needs UTF-8, and the ECI marker tells the scanner which encoding the bytes use.
         var utf8 = !IsLatin1(text);
         QRCodeData data;
         try {
            data = QRCodeGenerator.GenerateQrCode(text, ToEcc(options.ErrorCorrection), utf8, false,
               utf8 ? QRCodeGenerator.EciMode.Utf8 : QRCodeGenerator.EciMode.Default);
         } catch (DataTooLongException ex) {
            throw new ArgumentException(
               $"The text ({text.Length} characters) is too long for a QR code with error correction {options.ErrorCorrection}. " +
               "Shorten it or use a lower error correction level.", nameof(text), ex);
         }
         using (data) {
            var png = new PngByteQRCode(data);
            return png.GetGraphic(options.PixelsPerModule, ToRgb(options.Dark), ToRgb(options.Light), options.QuietZone);
         }
      }

      private static bool IsLatin1(string text) {
         foreach (var c in text) {
            if (c > 0xFF) return false;
         }
         return true;
      }

      private static QRCodeGenerator.ECCLevel ToEcc(QrErrorCorrection level) => level switch {
         QrErrorCorrection.L => QRCodeGenerator.ECCLevel.L,
         QrErrorCorrection.Q => QRCodeGenerator.ECCLevel.Q,
         QrErrorCorrection.H => QRCodeGenerator.ECCLevel.H,
         _ => QRCodeGenerator.ECCLevel.M,
      };

      private static byte[] ToRgb(QrColor color) => [color.R, color.G, color.B];
   }
}
