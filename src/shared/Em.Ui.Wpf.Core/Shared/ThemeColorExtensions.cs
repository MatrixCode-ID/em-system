using System.Windows;
using System.Windows.Media;
using Em.Ui.Core.Shared;
using Color = System.Windows.Media.Color;

namespace Em.Ui.Wpf.Shared
{
   internal static class ThemeColorExtensions
   {
      public static Color ToColor(this ThemeColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

      public static SolidColorBrush ToBrush(this ThemeColor color) {
         var brush = new SolidColorBrush(color.ToColor());
         brush.Freeze();
         return brush;
      }
   }

   // The engine's theme tokens: application-level resources rewritten from the active ThemeBase every
   // time the theme is applied, and read everywhere through DynamicResource. None of these keys may be
   // declared in a dictionary merged at element level (Styles/Palette.xaml and friends): DynamicResource
   // walks the element tree first, so a default there would hide the application-level value.
   internal static class ThemeResources
   {
      public const string AccentBrush = "themeAccentBrush";
      public const string OnAccentBrush = "themeOnAccentBrush";
      public const string WindowBackgroundBrush = "themeWindowBackgroundBrush";
      public const string WindowForegroundBrush = "themeWindowForegroundBrush";
      public const string PopupBackgroundBrush = "themePopupBackgroundBrush";
      public const string BrandBrush = "themeBrandBrush";
      public const string OnBrandBrush = "themeOnBrandBrush";

      public const string PrimaryContainerBrush = "themePrimaryContainerBrush";
      public const string OnPrimaryContainerBrush = "themeOnPrimaryContainerBrush";
      public const string SurfaceContainerLowBrush = "themeSurfaceContainerLowBrush";
      public const string SurfaceContainerBrush = "themeSurfaceContainerBrush";
      public const string OnSurfaceVariantBrush = "themeOnSurfaceVariantBrush";
      public const string OutlineBrush = "themeOutlineBrush";
      public const string OutlineVariantBrush = "themeOutlineVariantBrush";
      public const string ErrorBrush = "themeErrorBrush";
      public const string ErrorContainerBrush = "themeErrorContainerBrush";
      public const string OnErrorContainerBrush = "themeOnErrorContainerBrush";
      public const string ScrimBrush = "themeScrimBrush";
      public const string PrimaryContainerColor = "themePrimaryContainerColor";
      public const string SurfaceContainerLowColor = "themeSurfaceContainerLowColor";
      public const string SurfaceColor = "themeSurfaceColor";

      public static void Apply(ResourceDictionary resources, ThemeBase theme) {
         resources[PrimaryContainerBrush] = theme.PrimaryContainer.ToBrush();
         resources[OnPrimaryContainerBrush] = theme.OnPrimaryContainer.ToBrush();
         resources[SurfaceContainerLowBrush] = theme.SurfaceContainerLow.ToBrush();
         resources[SurfaceContainerBrush] = theme.SurfaceContainer.ToBrush();
         resources[OnSurfaceVariantBrush] = theme.OnSurfaceVariant.ToBrush();
         resources[OutlineBrush] = theme.Outline.ToBrush();
         resources[OutlineVariantBrush] = theme.OutlineVariant.ToBrush();
         resources[ErrorBrush] = theme.Error.ToBrush();
         resources[ErrorContainerBrush] = theme.ErrorContainer.ToBrush();
         resources[OnErrorContainerBrush] = theme.OnErrorContainer.ToBrush();
         resources[ScrimBrush] = theme.Scrim.ToBrush();
         resources[PrimaryContainerColor] = theme.PrimaryContainer.ToColor();
         resources[SurfaceContainerLowColor] = theme.SurfaceContainerLow.ToColor();
         resources[SurfaceColor] = theme.Surface.ToColor();

         resources[AccentBrush] = theme.Primary.ToBrush();
         resources[OnAccentBrush] = theme.OnPrimary.ToBrush();
         resources[WindowBackgroundBrush] = theme.Surface.ToBrush();
         resources[WindowForegroundBrush] = theme.OnSurface.ToBrush();
         resources[PopupBackgroundBrush] = theme.SurfaceContainerHigh.ToBrush();
         resources[BrandBrush] = theme.Brand.ToBrush();
         resources[OnBrandBrush] = theme.OnBrand.ToBrush();

         // The stock WPF styles give Button, ComboBox, TextBox and friends their Foreground from these
         // system keys, which stay black whatever the theme. Overriding them here is what keeps text
         // readable on the dark theme when no third-party theme restyles those controls.
         var text = theme.OnSurface.ToBrush();
         resources[SystemColors.ControlTextBrushKey] = text;
         resources[SystemColors.WindowTextBrushKey] = text;
      }
   }
}
