using System.Windows;
using System.Windows.Media;
using Em.Ui.Core.Shared;
using Em.Ui.Wpf.Documents.Themes;

namespace Em.Ui.Wpf.Documents
{
   /// <summary>
   /// Maps the active Em theme onto the palette of the document controls, so switching the Em theme also switches
   /// the grid, sheet, writer, editors, menus and print preview.
   /// </summary>
   /// <remarks>
   /// Registered by <see cref="DocumentsExtensions.UseDocuments"/>. It turns on
   /// <see cref="GridTheme.UseHostPalette"/> and writes every <c>MatrixGrid.Brush.*</c> brush into the application
   /// resources. Roles the Em theme has are taken from it; the others (tertiary container, lowest and highest
   /// surface containers, inverse surface) come from the built-in Light or Dark palette that matches
   /// <see cref="ThemeBase.Variant"/>. The state layers are derived from the Em colors. The mapping table is in
   /// doc/engine/engine-documents.md#theme.
   /// </remarks>
   public sealed class DocumentsThemeApplier : IThemeApplier
   {
      private const string KeyPrefix = "MatrixGrid.Brush.";

      /// <summary>
      /// Writes the palette brushes for <paramref name="theme"/> into the application resources. Does nothing when
      /// no WPF application is running.
      /// </summary>
      /// <param name="theme">The active Em theme.</param>
      public void Apply(ThemeBase theme) {
         ArgumentNullException.ThrowIfNull(theme);
         GridTheme.UseHostPalette = true;
         if (Application.Current is not { } application) {
            return;
         }
         foreach (var (key, color) in CreatePalette(theme)) {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            application.Resources[KeyPrefix + key] = brush;
         }
      }

      /// <summary>
      /// Builds the palette for a theme: every <c>MatrixGrid.Brush.*</c> role, without the prefix, and its color.
      /// </summary>
      /// <param name="theme">The Em theme.</param>
      /// <returns>The colors by role name.</returns>
      public static IReadOnlyDictionary<string, Color> CreatePalette(ThemeBase theme) {
         ArgumentNullException.ThrowIfNull(theme);
         bool dark = theme.Variant == ThemeVariant.Dark;
         var fallback = LoadBuiltInPalette(dark);
         var palette = new Dictionary<string, Color>(StringComparer.Ordinal);
         foreach (var (key, color) in fallback) {
            palette[key] = color;
         }

         palette["Primary"] = ToColor(theme.Primary);
         palette["OnPrimary"] = ToColor(theme.OnPrimary);
         palette["PrimaryContainer"] = ToColor(theme.PrimaryContainer);
         palette["OnPrimaryContainer"] = ToColor(theme.OnPrimaryContainer);
         palette["SecondaryContainer"] = ToColor(theme.SecondaryContainer);
         palette["OnSecondaryContainer"] = ToColor(theme.OnSecondaryContainer);
         palette["Success"] = ToColor(theme.Success);
         palette["Error"] = ToColor(theme.Error);
         palette["ErrorContainer"] = ToColor(theme.ErrorContainer);
         palette["Surface"] = ToColor(theme.Surface);
         palette["SurfaceContainerLow"] = ToColor(theme.SurfaceContainerLow);
         palette["SurfaceContainer"] = ToColor(theme.SurfaceContainer);
         palette["SurfaceContainerHigh"] = ToColor(theme.SurfaceContainerHigh);
         palette["OnSurface"] = ToColor(theme.OnSurface);
         palette["OnSurfaceVariant"] = ToColor(theme.OnSurfaceVariant);
         palette["Outline"] = ToColor(theme.Outline);
         palette["OutlineVariant"] = ToColor(theme.OutlineVariant);
         palette["Scrim"] = ToColor(theme.Scrim);

         // Material state layers: 8% on-surface for hover, a primary tint for selection, 38% on-surface for disabled.
         palette["StateHover"] = WithAlpha(theme.OnSurface, 0x14);
         palette["StateSelected"] = WithAlpha(theme.Primary, dark ? (byte)0x29 : (byte)0x1F);
         palette["StateDisabled"] = WithAlpha(theme.OnSurface, 0x61);
         return palette;
      }

      private static IEnumerable<(string Key, Color Color)> LoadBuiltInPalette(bool dark) {
         var dictionary = new ResourceDictionary {
            Source = new Uri($"/Em.Ui.Wpf.Documents;component/Themes/{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative)
         };
         foreach (var key in dictionary.Keys.OfType<string>()) {
            if (key.StartsWith(KeyPrefix, StringComparison.Ordinal) && dictionary[key] is SolidColorBrush brush) {
               yield return (key[KeyPrefix.Length..], brush.Color);
            }
         }
      }

      private static Color ToColor(ThemeColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

      private static Color WithAlpha(ThemeColor color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);
   }
}
