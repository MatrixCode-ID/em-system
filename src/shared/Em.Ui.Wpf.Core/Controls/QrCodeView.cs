using System.Windows;
using System.Windows.Media;
using Em.Shared;
using Em.Ui.Wpf.Shared;
using Control = System.Windows.Controls.Control;

namespace Em.Ui.Wpf.Controls
{
   /// <summary>
   /// Shows the QR code of <see cref="Text"/> as a square of <see cref="Size"/>, in the colors of the theme.
   /// </summary>
   /// <remarks>
   /// This control is lookless: its look comes from the default style in <c>Themes/Generic.xaml</c>. Its
   /// colors come from <c>Background</c> and <c>Foreground</c> (default the theme window tokens). Whatever
   /// the theme, the lighter of the two becomes the tile and the quiet zone and the darker one the modules,
   /// so the code is always dark on light and stays scannable in a dark theme. The image is redrawn when the
   /// text, the level or the theme changes. An empty <see cref="Text"/> shows an empty tile.
   /// <code>
   /// &lt;controls:QrCodeView Text="{Binding Serial}" Size="160" /&gt;
   /// </code>
   /// </remarks>
   public class QrCodeView : Control
   {
      static QrCodeView() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(QrCodeView), new FrameworkPropertyMetadata(typeof(QrCodeView)));
         BackgroundProperty.OverrideMetadata(typeof(QrCodeView), new FrameworkPropertyMetadata(Redraw));
         ForegroundProperty.OverrideMetadata(typeof(QrCodeView), new FrameworkPropertyMetadata(Redraw));
      }

      /// <summary>Creates the control and redraws it once its DPI is known.</summary>
      public QrCodeView() {
         Loaded += (_, _) => Update();
      }

      /// <inheritdoc />
      protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) {
         base.OnDpiChanged(oldDpi, newDpi);
         Update();
      }

      /// <summary>Identifies the <see cref="Text"/> property.</summary>
      public static readonly DependencyProperty TextProperty =
         DependencyProperty.Register(nameof(Text), typeof(string), typeof(QrCodeView),
            new FrameworkPropertyMetadata(null, Redraw));

      /// <summary>
      /// The text encoded in the code, for example a serial number. <c>null</c> or empty shows an empty tile;
      /// text too long for a QR code also shows an empty tile, with the reason in <see cref="Error"/>.
      /// </summary>
      public string? Text {
         get => (string?)GetValue(TextProperty);
         set => SetValue(TextProperty, value);
      }

      /// <summary>Identifies the <see cref="Size"/> property.</summary>
      public static readonly DependencyProperty SizeProperty =
         DependencyProperty.Register(nameof(Size), typeof(double), typeof(QrCodeView),
            new FrameworkPropertyMetadata(160.0, FrameworkPropertyMetadataOptions.AffectsMeasure, Redraw));

      /// <summary>Width and height of the square, in device-independent units, quiet zone included. Default 160.</summary>
      public double Size {
         get => (double)GetValue(SizeProperty);
         set => SetValue(SizeProperty, value);
      }

      /// <summary>Identifies the <see cref="ErrorCorrection"/> property.</summary>
      public static readonly DependencyProperty ErrorCorrectionProperty =
         DependencyProperty.Register(nameof(ErrorCorrection), typeof(QrErrorCorrection), typeof(QrCodeView),
            new FrameworkPropertyMetadata(QrErrorCorrection.M, Redraw));

      /// <summary>Error correction level of the code. Default <see cref="QrErrorCorrection.M"/>.</summary>
      public QrErrorCorrection ErrorCorrection {
         get => (QrErrorCorrection)GetValue(ErrorCorrectionProperty);
         set => SetValue(ErrorCorrectionProperty, value);
      }

      private static readonly DependencyPropertyKey ImageSourcePropertyKey =
         DependencyProperty.RegisterReadOnly(nameof(ImageSource), typeof(ImageSource), typeof(QrCodeView),
            new FrameworkPropertyMetadata(null));

      /// <summary>Identifies the read-only <see cref="ImageSource"/> property.</summary>
      public static readonly DependencyProperty ImageSourceProperty = ImageSourcePropertyKey.DependencyProperty;

      /// <summary>The drawn code, used by the template; <c>null</c> when there is nothing to show.</summary>
      public ImageSource? ImageSource => (ImageSource?)GetValue(ImageSourceProperty);

      private static readonly DependencyPropertyKey TileBrushPropertyKey =
         DependencyProperty.RegisterReadOnly(nameof(TileBrush), typeof(Brush), typeof(QrCodeView),
            new FrameworkPropertyMetadata(null));

      /// <summary>Identifies the read-only <see cref="TileBrush"/> property.</summary>
      public static readonly DependencyProperty TileBrushProperty = TileBrushPropertyKey.DependencyProperty;

      /// <summary>The lighter theme color, painted behind the code; used by the template.</summary>
      public Brush? TileBrush => (Brush?)GetValue(TileBrushProperty);

      /// <summary>Why the last text could not be drawn (for example too long), or <c>null</c> when it was.</summary>
      public string? Error { get; private set; }

      private static void Redraw(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((QrCodeView)d).Update();

      private void Update() {
         var a = QrCodeImage.ToQrColor(Background, QrColor.White);
         var b = QrCodeImage.ToQrColor(Foreground, QrColor.Black);
         var (light, dark) = a.Luminance >= b.Luminance ? (a, b) : (b, a);
         SetValue(TileBrushPropertyKey, Frozen(light));
         Error = null;

         var text = Text;
         if (string.IsNullOrEmpty(text) || Size <= 0) {
            SetValue(ImageSourcePropertyKey, null);
            return;
         }
         try {
            var options = new QrCodeOptions { ErrorCorrection = ErrorCorrection, PixelsPerModule = 1, Dark = dark, Light = light };
            // One pixel per module gives the module count; the real image is then drawn just large enough
            // for the size on this screen, so the template only scales it down a little.
            var modules = QrCodeImage.Create(text, options).PixelWidth;
            var scale = IsLoaded ? VisualTreeHelper.GetDpi(this).DpiScaleX : 1.0;
            var perModule = Math.Clamp((int)Math.Ceiling(Size * scale / modules),
               QrCodeOptions.MinPixelsPerModule, QrCodeOptions.MaxPixelsPerModule);
            SetValue(ImageSourcePropertyKey, QrCodeImage.Create(text, options with { PixelsPerModule = perModule }));
         } catch (ArgumentException ex) {
            SetValue(ImageSourcePropertyKey, null);
            Error = ex.Message;
         }
      }

      private static SolidColorBrush Frozen(QrColor color) {
         var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
         brush.Freeze();
         return brush;
      }
   }
}
