using Em.Shared;
using ZXing;
using ZXing.Common;

namespace Em.Libs.Tests
{
   public class QrCodeServiceTests
   {
      private static readonly QrCodeService Service = QrCodeService.Default;

      private static Result Read(byte[] png) {
         var image = PngTestDecoder.Decode(png);
         var source = new RGBLuminanceSource(image.Gray(), image.Width, image.Height, RGBLuminanceSource.BitmapFormat.Gray8);
         var reader = new BarcodeReaderGeneric {
            Options = new DecodingOptions { PossibleFormats = [BarcodeFormat.QR_CODE], TryHarder = true },
         };
         var result = reader.Decode(source);
         Assert.NotNull(result);
         return result;
      }

      [Theory]
      [InlineData("0123456789")] // numeric
      [InlineData("SERIAL-42 AB$%*+./:")] // alphanumeric
      [InlineData("Mixed case text, with punctuation!")] // byte, ISO-8859-1
      [InlineData("Ünïcode ✓ 二维码 😀")] // byte, UTF-8 with ECI
      public void CreatePng_DecodesToOriginalText(string text) {
         Assert.Equal(text, Read(Service.CreatePng(text)).Text);
      }

      [Fact]
      public void CreatePng_DefaultsAreMediumTenPixelsWithQuietZone() {
         var one = PngTestDecoder.Decode(Service.CreatePng("0123456789", new QrCodeOptions { PixelsPerModule = 1 }));
         var standard = PngTestDecoder.Decode(Service.CreatePng("0123456789"));
         Assert.Equal(one.Width * 10, standard.Width);
         Assert.Equal(standard.Width, standard.Height);
         Assert.Equal((byte)255, standard.Pixel(0, 0).R); // quiet zone is white
         Assert.Equal((0, 0, 0), standard.Pixel(40, 40)); // finder pattern corner after 4 quiet modules
         Assert.Equal("M", Read(Service.CreatePng("0123456789")).ResultMetadata[ResultMetadataType.ERROR_CORRECTION_LEVEL]);
      }

      [Theory]
      [InlineData(QrErrorCorrection.L, "L")]
      [InlineData(QrErrorCorrection.M, "M")]
      [InlineData(QrErrorCorrection.Q, "Q")]
      [InlineData(QrErrorCorrection.H, "H")]
      public void CreatePng_HonorsErrorCorrection(QrErrorCorrection level, string expected) {
         var result = Read(Service.CreatePng("0123456789", new QrCodeOptions { ErrorCorrection = level }));
         Assert.Equal(expected, result.ResultMetadata[ResultMetadataType.ERROR_CORRECTION_LEVEL]);
      }

      [Fact]
      public void CreatePng_HonorsPixelsPerModuleAndQuietZone() {
         var with = PngTestDecoder.Decode(Service.CreatePng("ABC", new QrCodeOptions { PixelsPerModule = 3 }));
         var without = PngTestDecoder.Decode(Service.CreatePng("ABC", new QrCodeOptions { PixelsPerModule = 3, QuietZone = false }));
         Assert.Equal(0, with.Width % 3);
         Assert.Equal(with.Width - 8 * 3, without.Width); // 4 modules on each side
         Assert.Equal((0, 0, 0), without.Pixel(0, 0)); // the finder pattern starts at the edge
      }

      [Fact]
      public void CreatePng_HonorsColors() {
         var options = new QrCodeOptions { PixelsPerModule = 2, Dark = new QrColor(20, 40, 90), Light = new QrColor(250, 240, 200) };
         var png = Service.CreatePng("0123456789", options);
         var image = PngTestDecoder.Decode(png);
         Assert.Equal((250, 240, 200), image.Pixel(0, 0));
         Assert.Equal((20, 40, 90), image.Pixel(8, 8));
         Assert.Equal("0123456789", Read(png).Text);
      }

      [Fact]
      public void CreatePng_RejectsNull() {
         Assert.Throws<ArgumentNullException>(() => Service.CreatePng(null!));
      }

      [Fact]
      public void CreatePng_RejectsEmpty() {
         var ex = Assert.Throws<ArgumentException>(() => Service.CreatePng(""));
         Assert.Equal("text", ex.ParamName);
      }

      [Fact]
      public void CreatePng_RejectsTextTooLongWithClearMessage() {
         var ex = Assert.Throws<ArgumentException>(() => Service.CreatePng(new string('9', 8000)));
         Assert.Equal("text", ex.ParamName);
         Assert.Contains("too long", ex.Message);
      }

      [Fact]
      public void CreatePng_TooLongDependsOnErrorCorrection() {
         // The largest code holds 7089 digits at L but only 3057 at H.
         var digits = new string('7', 4000);
         Assert.NotEmpty(Service.CreatePng(digits, new QrCodeOptions { ErrorCorrection = QrErrorCorrection.L }));
         Assert.Throws<ArgumentException>(() => Service.CreatePng(digits, new QrCodeOptions { ErrorCorrection = QrErrorCorrection.H }));
      }

      [Theory]
      [InlineData(0)]
      [InlineData(101)]
      public void CreatePng_RejectsPixelsPerModuleOutOfRange(int pixels) {
         Assert.Throws<ArgumentOutOfRangeException>(() => Service.CreatePng("1", new QrCodeOptions { PixelsPerModule = pixels }));
      }

      [Fact]
      public void Luminance_OrdersLightAboveDark() {
         Assert.True(QrColor.White.Luminance > new QrColor(226, 226, 230).Luminance);
         Assert.True(new QrColor(226, 226, 230).Luminance > new QrColor(17, 20, 24).Luminance);
         Assert.Equal(0, QrColor.Black.Luminance);
      }
   }
}
