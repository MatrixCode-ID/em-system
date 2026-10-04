using Microsoft.Maui.Controls;
using Em.Ui.Core.Shared;

namespace Em.Ui.Maui.Shared
{
   // Turns BrandingInfo.LogoSource into something an Image can show. MAUI turns an .svg into a
   // bitmap at build time, so a file name is all there is to resolve here.
   internal static class BrandingImages
   {
      // Em.Ui.Maui.Core's own logo (Resources/Images/em_logo.svg). MAUI resource names are always
      // lower case and end in .png, because an .svg is turned into a bitmap at build time.
      private const string DefaultLogoSource = "em_logo.png";

      private static readonly Dictionary<string, ImageSource> Cache = [];

      public static ImageSource LoadLogo(BrandingInfo branding) {
         var source = string.IsNullOrWhiteSpace(branding.LogoSource) ? DefaultLogoSource : branding.LogoSource;

         lock (Cache) {
            if (!Cache.TryGetValue(source, out var image)) {
               image = ImageSource.FromFile(source);
               Cache[source] = image;
            }

            return image;
         }
      }
   }
}
