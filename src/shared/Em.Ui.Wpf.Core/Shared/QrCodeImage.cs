using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Em.Shared;

namespace Em.Ui.Wpf.Shared
{
   /// <summary>
   /// Turns a text into a WPF image of its QR code, on top of <see cref="IQrCodeService"/>. For a ready-made
   /// themed control use <see cref="Controls.QrCodeView"/>.
   /// </summary>
   /// <remarks>
   /// The returned image is frozen, so it can be created on any thread and bound from the UI thread.
   /// <code>
   /// var qr = app.ServiceProvider.GetRequiredService&lt;IQrCodeService&gt;();
   /// QrImage = QrCodeImage.Create("0123456789", service: qr);
   /// </code>
   /// </remarks>
   public static class QrCodeImage
   {
      /// <summary>Encodes <paramref name="text"/> and returns the QR code as a frozen <see cref="BitmapSource"/>.</summary>
      /// <param name="text">The text to encode.</param>
      /// <param name="options">How the image is drawn; <c>null</c> means <see cref="QrCodeOptions.Default"/>.</param>
      /// <param name="service">
      /// The service that encodes it; <c>null</c> uses <see cref="QrCodeService.Default"/>, the instance the
      /// engine registers.
      /// </param>
      /// <returns>The image, one pixel per PNG pixel.</returns>
      /// <exception cref="ArgumentException">See <see cref="IQrCodeService.CreatePng"/>.</exception>
      public static BitmapSource Create(string text, QrCodeOptions? options = null, IQrCodeService? service = null) {
         return FromPng((service ?? QrCodeService.Default).CreatePng(text, options));
      }

      /// <summary>Decodes PNG bytes made by <see cref="IQrCodeService.CreatePng"/> into a frozen <see cref="BitmapSource"/>.</summary>
      /// <param name="png">The PNG bytes.</param>
      /// <returns>The decoded image.</returns>
      public static BitmapSource FromPng(byte[] png) {
         ArgumentNullException.ThrowIfNull(png);
         using var stream = new MemoryStream(png);
         var image = new BitmapImage();
         image.BeginInit();
         image.CacheOption = BitmapCacheOption.OnLoad;
         image.StreamSource = stream;
         image.EndInit();
         image.Freeze();
         return image;
      }

      /// <summary>
      /// Converts a brush to a <see cref="QrColor"/>: the color of a <see cref="SolidColorBrush"/>, or
      /// <paramref name="fallback"/> for any other brush or <c>null</c>.
      /// </summary>
      /// <param name="brush">The brush, usually a theme token.</param>
      /// <param name="fallback">The color used when the brush has no single color.</param>
      /// <returns>The opaque color.</returns>
      public static QrColor ToQrColor(Brush? brush, QrColor fallback) {
         return brush is SolidColorBrush solid ? new QrColor(solid.Color.R, solid.Color.G, solid.Color.B) : fallback;
      }
   }
}
