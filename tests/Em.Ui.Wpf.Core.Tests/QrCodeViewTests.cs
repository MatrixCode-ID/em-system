using System.Windows.Media;
using System.Windows.Media.Imaging;
using Em.Shared;
using Em.Ui.Wpf.Controls;
using Em.Ui.Wpf.Shared;

namespace Em.Ui.Wpf.Core.Tests;

public class QrCodeViewTests
{
   [Fact]
   public void QrCodeImage_Create_ReturnsFrozenImageOfThePng() {
      var png = QrCodeService.Default.CreatePng("0123456789", new QrCodeOptions { PixelsPerModule = 4 });
      var image = QrCodeImage.Create("0123456789", new QrCodeOptions { PixelsPerModule = 4 });
      Assert.True(image.IsFrozen);
      Assert.Equal(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)), image.PixelWidth);
      Assert.Equal(image.PixelWidth, image.PixelHeight);
   }

   [Fact]
   public void QrCodeImage_ToQrColor_UsesSolidColorOrFallback() {
      Assert.Equal(new QrColor(1, 2, 3), QrCodeImage.ToQrColor(new SolidColorBrush(Color.FromRgb(1, 2, 3)), QrColor.White));
      Assert.Equal(QrColor.White, QrCodeImage.ToQrColor(new LinearGradientBrush(), QrColor.White));
      Assert.Equal(QrColor.Black, QrCodeImage.ToQrColor(null, QrColor.Black));
   }

   [WpfTheory]
   [InlineData(250, 249, 253, 26, 28, 30)] // light theme: background is the tile
   [InlineData(17, 20, 24, 226, 226, 230)] // dark theme: the light foreground becomes the tile
   public void QrCodeView_DrawsDarkOnLightInBothThemes(byte br, byte bg, byte bb, byte fr, byte fg, byte fb) {
      var view = new QrCodeView {
         Background = new SolidColorBrush(Color.FromRgb(br, bg, bb)),
         Foreground = new SolidColorBrush(Color.FromRgb(fr, fg, fb)),
         Text = "0123456789",
      };
      var tile = Assert.IsType<SolidColorBrush>(view.TileBrush);
      var image = Assert.IsAssignableFrom<BitmapSource>(view.ImageSource);
      var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
      var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
      bgra.CopyPixels(pixels, image.PixelWidth * 4, 0);
      var corner = Color.FromRgb(pixels[2], pixels[1], pixels[0]);
      Assert.Equal(tile.Color, corner);
      var lighter = br > fr ? Color.FromRgb(br, bg, bb) : Color.FromRgb(fr, fg, fb);
      Assert.Equal(lighter, tile.Color);
   }

   [WpfFact]
   public void QrCodeView_EmptyOrTooLongText_ShowsNothing() {
      var view = new QrCodeView { Text = "" };
      Assert.Null(view.ImageSource);
      Assert.Null(view.Error);
      view.Text = new string('9', 8000);
      Assert.Null(view.ImageSource);
      Assert.Contains("too long", view.Error);
      view.Text = "1";
      Assert.NotNull(view.ImageSource);
      Assert.Null(view.Error);
   }

   [WpfFact]
   public void QrCodeView_ImageCoversSize() {
      var view = new QrCodeView { Text = "0123456789", Size = 200 };
      Assert.True(((BitmapSource)view.ImageSource!).PixelWidth >= 200);
      Assert.Equal(QrErrorCorrection.M, view.ErrorCorrection);
   }
}
