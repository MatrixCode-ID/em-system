using System.Windows;
using System.Windows.Media;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Documents.Tests
{
   /// <summary>
   /// Checks how the active Em theme is mapped onto the palette of the document controls.
   /// </summary>
   public class DocumentsThemeApplierTests
   {
      private static readonly string[] Roles =
      [
         "Primary", "OnPrimary", "Success", "PrimaryContainer", "OnPrimaryContainer", "SecondaryContainer",
         "OnSecondaryContainer", "TertiaryContainer", "OnTertiaryContainer", "Error", "ErrorContainer", "Surface",
         "SurfaceContainerLowest", "SurfaceContainerLow", "SurfaceContainer", "SurfaceContainerHigh",
         "SurfaceContainerHighest", "OnSurface", "OnSurfaceVariant", "Outline", "OutlineVariant", "InverseSurface",
         "InverseOnSurface", "StateHover", "StateSelected", "StateDisabled", "Scrim"
      ];

      [Fact]
      public void Palette_covers_every_role_and_takes_em_colors() {
         DocumentsBoundaryTests.RunOnSta(() => {
            EnsurePackScheme();
            var theme = new TestTheme(ThemeVariant.Light) { Primary = ThemeColor.FromRgb(0x12, 0x34, 0x56), Surface = ThemeColor.FromRgb(1, 2, 3) };
            var palette = DocumentsThemeApplier.CreatePalette(theme);
            Assert.Equal(Roles.OrderBy(role => role), palette.Keys.OrderBy(role => role));
            Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), palette["Primary"]);
            Assert.Equal(Color.FromRgb(1, 2, 3), palette["Surface"]);
            Assert.Equal(Color.FromArgb(0x1F, 0x12, 0x34, 0x56), palette["StateSelected"]);
         });
      }

      [Fact]
      public void Roles_missing_from_em_follow_the_variant() {
         DocumentsBoundaryTests.RunOnSta(() => {
            EnsurePackScheme();
            var light = DocumentsThemeApplier.CreatePalette(new TestTheme(ThemeVariant.Light));
            var dark = DocumentsThemeApplier.CreatePalette(new TestTheme(ThemeVariant.Dark));
            Assert.Equal((Color)ColorConverter.ConvertFromString("#FAD8FD"), light["TertiaryContainer"]);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#573E5C"), dark["TertiaryContainer"]);
            Assert.Equal((Color)ColorConverter.ConvertFromString("#2E3036"), light["InverseSurface"]);
         });
      }

      private static void EnsurePackScheme() {
         // Touching the pack scheme registers it, so relative component URIs resolve without a running Application.
         _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
         if (Application.ResourceAssembly is null) {
            Application.ResourceAssembly = typeof(DocumentsThemeApplierTests).Assembly;
         }
      }

      private sealed class TestTheme(ThemeVariant variant) : ThemeBase(variant);
   }
}
