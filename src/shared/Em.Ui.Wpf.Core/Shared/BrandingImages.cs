using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Em.Ui.Core.Shared;

namespace Em.Ui.Wpf.Shared
{
   // Turns BrandingInfo.LogoSource into something an Image can show. The core itself only knows
   // bitmaps; any other format goes through the ILogoImageLoader instances the application registered.
   internal static class BrandingImages
   {
      private const string DefaultLogoSource =
         "pack://application:,,,/Em.Ui.Wpf.Core;component/Assets/Icons/Logo.png";

      private static readonly Dictionary<string, ImageSource> Cache = [];

      public static ImageSource LoadLogo(BrandingInfo branding, IServiceProvider services) {
         var source = string.IsNullOrWhiteSpace(branding.LogoSource) ? DefaultLogoSource : branding.LogoSource;

         lock (Cache) {
            if (Cache.TryGetValue(source, out var cached)) return cached;

            var image = TryLoad(source, services) ?? LoadBitmap(new Uri(DefaultLogoSource));
            Cache[source] = image;
            return image;
         }
      }

      private static readonly Dictionary<string, ImageSource> BackgroundCache = [];

      public static ImageSource? LoadLoginBackground(BrandingInfo branding, ThemeVariant variant,
         IServiceProvider services, out bool dimmed) {
         dimmed = false;
         var dark = variant == ThemeVariant.Dark;
         var own = dark ? branding.DarkLoginBackground : branding.LightLoginBackground;
         var other = dark ? branding.LightLoginBackground : branding.DarkLoginBackground;
         var image = TryLoadBackground(own, services);
         if (image is not null) return image;
         image = TryLoadBackground(other, services);
         dimmed = dark && image is not null;
         return image;
      }

      private static ImageSource? TryLoadBackground(string? source, IServiceProvider services) {
         if (string.IsNullOrWhiteSpace(source)) return null;
         lock (BackgroundCache) {
            var key = "bg:" + source;
            if (BackgroundCache.TryGetValue(key, out var cached)) return cached;
            try {
               var uri = new Uri(source, UriKind.RelativeOrAbsolute);
               ImageSource? image = null;
               foreach (var loader in services.GetServices<ILogoImageLoader>()) {
                  try { image = loader.TryLoad(uri); }
                  catch (Exception) { /* A failing extension must not prevent the bitmap fallback. */ }
                  if (image is not null) break;
               }
               if (image is null) {
                  var frame = BitmapFrame.Create(uri, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
                  var bitmap = new BitmapImage();
                  bitmap.BeginInit();
                  bitmap.CacheOption = BitmapCacheOption.OnLoad;
                  bitmap.DecodePixelWidth = Math.Min(frame.PixelWidth, 1920);
                  bitmap.UriSource = uri;
                  bitmap.EndInit();
                  image = bitmap;
               }
               if (image.CanFreeze) image.Freeze();
               BackgroundCache[key] = image;
               return image;
            }
            catch (Exception) { return null; }
         }
      }

      // The window icon from BrandingInfo.IconSource, or null when none is set or it cannot be loaded -
      // the window then keeps the built-in icon. A BitmapFrame (not a BitmapImage) so that an .ico with
      // several sizes lets Windows pick the right one for the taskbar and the title bar.
      public static ImageSource? LoadIcon(BrandingInfo branding) {
         if (string.IsNullOrWhiteSpace(branding.IconSource)) return null;

         lock (Cache) {
            if (Cache.TryGetValue("icon:" + branding.IconSource, out var cached)) return cached;

            try {
               var icon = BitmapFrame.Create(new Uri(branding.IconSource, UriKind.RelativeOrAbsolute),
                  BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
               icon.Freeze();
               Cache["icon:" + branding.IconSource] = icon;
               return icon;
            }
            catch (Exception) {
               return null;
            }
         }
      }

      // A logo is only decoration: a mistyped URI or a resource that was not built in must not stop
      // the application, so every failure falls back to the built-in logo.
      private static ImageSource? TryLoad(string source, IServiceProvider services) {
         try {
            var uri = new Uri(source, UriKind.RelativeOrAbsolute);

            foreach (var loader in services.GetServices<ILogoImageLoader>()) {
               if (loader.TryLoad(uri) is { } image) return image;
            }

            return LoadBitmap(uri);
         }
         catch (Exception) {
            return null;
         }
      }

      private static ImageSource LoadBitmap(Uri uri) {
         var bitmap = new BitmapImage();
         bitmap.BeginInit();
         // Decoded now rather than on first render, so a broken file fails here and falls back.
         bitmap.CacheOption = BitmapCacheOption.OnLoad;
         bitmap.UriSource = uri;
         bitmap.EndInit();
         bitmap.Freeze();
         return bitmap;
      }
   }
}
