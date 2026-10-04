using Microsoft.Maui.Controls;
using Em.Ui.Core.Shared;
using Color = Microsoft.Maui.Graphics.Color;

namespace Em.Ui.Maui.Styles
{
   /// <summary>
   /// Kamus warna tema aplikasi MAUI: sepasang kunci per peran warna, berakhiran <c>Light</c> dan
   /// <c>Dark</c>. Nilai di berkas XAML-nya adalah palet standar Em; saat kamus ini dibuat, nilainya
   /// ditimpa dengan tema terang dan gelap yang dipasang aplikasi lewat <c>ApplyBranding</c>.
   /// </summary>
   public partial class Palette : ResourceDictionary
   {
      /// <summary>
      /// Membuat kamus warna dan mengisinya dari tema aplikasi.
      /// </summary>
      public Palette() {
         InitializeComponent();

         if (Branding is { } branding) {
            Apply(branding.LightTheme, "Light");
            Apply(branding.DarkTheme, "Dark");
         }
      }

      // Set by EmApp.BuildApp, which always runs before App builds its resources. Every style reads
      // its colours once, while it loads, so the values have to be in place before that - writing them
      // into the application resources later would reach nothing.
      internal static BrandingInfo? Branding { get; set; }

      // Every colour role of the theme lands on the key of the same name, e.g. Primary on primaryLight
      // and primaryDark. Roles the palette has no key for are left out rather than added: the XAML is
      // the list of what the screens actually use.
      private void Apply(ThemeBase theme, string suffix) {
         foreach (var property in typeof(ThemeBase).GetProperties()) {
            if (property.PropertyType != typeof(ThemeColor)) continue;

            var key = char.ToLowerInvariant(property.Name[0]) + property.Name[1..] + suffix;
            if (!ContainsKey(key)) continue;

            var color = (ThemeColor)property.GetValue(theme)!;
            this[key] = Color.FromRgba(color.R, color.G, color.B, color.A);
         }
      }
   }
}
