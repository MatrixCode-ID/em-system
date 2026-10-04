namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Palet standar Em untuk mode gelap, pasangan <see cref="LightTheme"/>. Dipakai
   /// <see cref="BrandingInfo.DarkTheme"/> kalau aplikasi tidak memasang tema gelap sendiri.
   /// </summary>
   /// <remarks>
   /// Kelas ini sengaja tidak <c>sealed</c>: aplikasi boleh menurunkannya untuk mengganti sebagian
   /// warna, atau cukup memakai object initializer.
   /// </remarks>
   public class DarkTheme : ThemeBase
   {
      /// <summary>
      /// Membuat tema gelap dengan palet standar Em.
      /// </summary>
      public DarkTheme() : base(ThemeVariant.Dark) {
         Primary = ThemeColor.Parse("#A6C8FF");
         OnPrimary = ThemeColor.Parse("#00315F");
         PrimaryContainer = ThemeColor.Parse("#004787");
         OnPrimaryContainer = ThemeColor.Parse("#D6E3FF");
         Secondary = ThemeColor.Parse("#4CDADB");
         OnSecondary = ThemeColor.Parse("#003738");
         SecondaryContainer = ThemeColor.Parse("#004F50");
         OnSecondaryContainer = ThemeColor.Parse("#6FF7F8");
         Surface = ThemeColor.Parse("#111418");
         OnSurface = ThemeColor.Parse("#E2E2E6");
         OnSurfaceVariant = ThemeColor.Parse("#C3C6CF");
         SurfaceContainerLow = ThemeColor.Parse("#191C20");
         SurfaceContainer = ThemeColor.Parse("#1D2024");
         SurfaceContainerHigh = ThemeColor.Parse("#272A2F");
         Outline = ThemeColor.Parse("#8D9199");
         OutlineVariant = ThemeColor.Parse("#43474E");
         Error = ThemeColor.Parse("#FFB4AB");
         ErrorContainer = ThemeColor.Parse("#93000A");
         OnErrorContainer = ThemeColor.Parse("#FFDAD6");
         // Status colours are mid-tone on purpose, so the same value reads on both variants.
         Success = ThemeColor.Parse("#2FA84F");
         Warning = ThemeColor.Parse("#C98A00");
         Info = ThemeColor.Parse("#3B82F6");
         Scrim = ThemeColor.Parse("#000000");
         Brand = ThemeColor.Parse("#12283D");
         OnBrand = ThemeColor.Parse("#4FA3FF");
      }
   }
}
