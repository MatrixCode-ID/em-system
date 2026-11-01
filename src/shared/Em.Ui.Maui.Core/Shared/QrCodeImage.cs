using System.Buffers.Binary;
using Em.Shared;
using Color = Microsoft.Maui.Graphics.Color;

namespace Em.Ui.Maui.Shared
{
   /// <summary>
   /// Turns a text into a MAUI image of its QR code, on top of <see cref="IQrCodeService"/>. For a ready-made
   /// themed control use <see cref="Controls.QrCodeView"/>.
   /// </summary>
   /// <remarks>
   /// <code>
   /// var qr = app.ServiceProvider.GetRequiredService&lt;IQrCodeService&gt;();
   /// QrImage = QrCodeImage.Create("0123456789", service: qr);
   /// </code>
   /// </remarks>
   public static class QrCodeImage
   {
      /// <summary>Encodes <paramref name="text"/> and returns the QR code as an <see cref="ImageSource"/>.</summary>
      /// <param name="text">The text to encode.</param>
      /// <param name="options">How the image is drawn; <c>null</c> means <see cref="QrCodeOptions.Default"/>.</param>
      /// <param name="service">
      /// The service that encodes it; <c>null</c> uses <see cref="QrCodeService.Default"/>, the instance the
      /// engine registers.
      /// </param>
      /// <returns>The image, read from the PNG bytes each time the platform loads it.</returns>
      /// <exception cref="ArgumentException">See <see cref="IQrCodeService.CreatePng"/>.</exception>
      public static ImageSource Create(string text, QrCodeOptions? options = null, IQrCodeService? service = null) {
         return FromPng((service ?? QrCodeService.Default).CreatePng(text, options));
      }

      /// <summary>Wraps PNG bytes made by <see cref="IQrCodeService.CreatePng"/> in an <see cref="ImageSource"/>.</summary>
      /// <param name="png">The PNG bytes.</param>
      /// <returns>The image source.</returns>
      public static ImageSource FromPng(byte[] png) {
         ArgumentNullException.ThrowIfNull(png);
         return ImageSource.FromStream(() => new MemoryStream(png, false));
      }

      /// <summary>
      /// Converts a MAUI color to an opaque <see cref="QrColor"/>; <paramref name="fallback"/> for <c>null</c>.
      /// </summary>
      /// <param name="color">The color, usually a palette role.</param>
      /// <param name="fallback">The color used when there is none.</param>
      /// <returns>The opaque color.</returns>
      public static QrColor ToQrColor(Color? color, QrColor fallback) {
         if (color is null) return fallback;
         color.ToRgb(out var r, out var g, out var b);
         return new QrColor(r, g, b);
      }

      // Width in pixels from the PNG header (IHDR always follows the 8-byte signature), so the module count
      // is known without decoding the image on Android.
      internal static int PngWidth(byte[] png) => BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
   }
}
