using Em.Shared;
using Em.Ui.Maui.Shared;
using Microsoft.Maui.Controls.Shapes;
using Color = Microsoft.Maui.Graphics.Color;

namespace Em.Ui.Maui.Controls
{
   /// <summary>
   /// Shows the QR code of <see cref="Text"/> as a square of <see cref="Size"/>, in the colors of the theme.
   /// </summary>
   /// <remarks>
   /// The colors are the <c>surface</c> and <c>onSurface</c> roles of the palette for the current theme.
   /// Whatever the theme, the lighter of the two becomes the tile and the quiet zone and the darker one the
   /// modules, so the code is always dark on light and stays scannable in a dark theme. The image is redrawn
   /// when the text, the level or the theme changes. An empty <see cref="Text"/> shows an empty tile.
   /// <code>
   /// &lt;controls:QrCodeView Text="{Binding Serial}" Size="160" /&gt;
   /// </code>
   /// </remarks>
   public sealed class QrCodeView : ContentView
   {
      /// <summary>
      /// The text encoded in the code, for example a serial number. <c>null</c> or empty shows an empty tile;
      /// text too long for a QR code also shows an empty tile, with the reason in <see cref="Error"/>.
      /// </summary>
      public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(QrCodeView), null, propertyChanged: Changed);
      /// <summary>Width and height of the square, in device-independent units, quiet zone included. Default 160.</summary>
      public static readonly BindableProperty SizeProperty = BindableProperty.Create(nameof(Size), typeof(double), typeof(QrCodeView), 160.0, propertyChanged: Changed);
      /// <summary>Error correction level of the code. Default <see cref="QrErrorCorrection.M"/>.</summary>
      public static readonly BindableProperty ErrorCorrectionProperty = BindableProperty.Create(nameof(ErrorCorrection), typeof(QrErrorCorrection), typeof(QrCodeView), QrErrorCorrection.M, propertyChanged: Changed);
      /// <summary>
      /// Width of the light border around the code, in device-independent units. <c>null</c> (default)
      /// draws the standard quiet zone of 4 modules; a number replaces it with a border of that width, for example
      /// 6 (about 1 mm) when the area around the tile is already light and empty. The code then fills more of
      /// <see cref="Size"/>.
      /// </summary>
      public static readonly BindableProperty QuietZoneSizeProperty = BindableProperty.Create(nameof(QuietZoneSize), typeof(double?), typeof(QrCodeView), null, propertyChanged: Changed);

      private static readonly Lazy<Styles.Palette> SharedPalette = new(() => new Styles.Palette());
      private readonly Image _image = new() { Aspect = Aspect.AspectFit };
      private readonly Border _tile;

      /// <summary>Creates the control.</summary>
      public QrCodeView() {
         _tile = new Border {
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 0,
            Content = _image,
         };
         Content = _tile;
         Loaded += (_, _) => {
            if (Application.Current is { } app) app.RequestedThemeChanged += OnThemeChanged;
            Update();
         };
         Unloaded += (_, _) => {
            if (Application.Current is { } app) app.RequestedThemeChanged -= OnThemeChanged;
         };
         Update();
      }

      /// <inheritdoc cref="TextProperty"/>
      public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
      /// <inheritdoc cref="SizeProperty"/>
      public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
      /// <inheritdoc cref="ErrorCorrectionProperty"/>
      public QrErrorCorrection ErrorCorrection { get => (QrErrorCorrection)GetValue(ErrorCorrectionProperty); set => SetValue(ErrorCorrectionProperty, value); }
      /// <inheritdoc cref="QuietZoneSizeProperty"/>
      public double? QuietZoneSize { get => (double?)GetValue(QuietZoneSizeProperty); set => SetValue(QuietZoneSizeProperty, value); }

      /// <summary>Why the last text could not be drawn (for example too long), or <c>null</c> when it was.</summary>
      public string? Error { get; private set; }

      private static void Changed(BindableObject b, object o, object n) => ((QrCodeView)b).Update();

      private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Update();

      private void Update() {
         var variant = Application.Current?.RequestedTheme == AppTheme.Dark ? "Dark" : "Light";
         var palette = SharedPalette.Value;
         var a = QrCodeImage.ToQrColor(palette["surface" + variant] as Color, QrColor.White);
         var b = QrCodeImage.ToQrColor(palette["onSurface" + variant] as Color, QrColor.Black);
         var (light, dark) = a.Luminance >= b.Luminance ? (a, b) : (b, a);

         // A custom border replaces the quiet zone drawn into the image with padding of the tile's own color.
         var border = QuietZoneSize is { } custom ? Math.Clamp(custom, 0, Size / 2) : 0;
         _tile.WidthRequest = _tile.HeightRequest = Size;
         _tile.Padding = border;
         _tile.BackgroundColor = Color.FromRgb(light.R, light.G, light.B);
         Error = null;

         var text = Text;
         if (string.IsNullOrEmpty(text) || Size <= 0) {
            _image.Source = null;
            return;
         }
         try {
            var options = new QrCodeOptions { ErrorCorrection = ErrorCorrection, PixelsPerModule = 1, Dark = dark, Light = light, QuietZone = QuietZoneSize is null };
            // One pixel per module gives the module count; the real image is then drawn just large enough
            // for the size on this screen, so the image view only scales it down a little.
            var modules = QrCodeImage.PngWidth(QrCodeService.Default.CreatePng(text, options));
            var density = DeviceDisplay.Current.MainDisplayInfo.Density is > 0 and var d ? d : 1.0;
            var perModule = Math.Clamp((int)Math.Ceiling((Size - 2 * border) * density / modules),
               QrCodeOptions.MinPixelsPerModule, QrCodeOptions.MaxPixelsPerModule);
            _image.Source = QrCodeImage.Create(text, options with { PixelsPerModule = perModule });
         } catch (ArgumentException ex) {
            _image.Source = null;
            Error = ex.Message;
         }
      }
   }
}
