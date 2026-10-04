namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Palet standar Em untuk mode terang, diturunkan dari warna merek: biru <c>#0F6CBD</c> sebagai
   /// warna utama dan teal sebagai warna pendamping. Dipakai <see cref="BrandingInfo.LightTheme"/>
   /// kalau aplikasi tidak memasang tema terang sendiri.
   /// </summary>
   /// <remarks>
   /// Kelas ini sengaja tidak <c>sealed</c>: aplikasi boleh menurunkannya untuk mengganti sebagian
   /// warna, atau cukup memakai object initializer, mis.
   /// <c>new LightTheme { Brand = ThemeColor.Parse("#7A1F2B") }</c>.
   /// </remarks>
   public class LightTheme : ThemeBase
   {
      /// <summary>
      /// Membuat tema terang dengan palet standar Em.
      /// </summary>
      public LightTheme() : base(ThemeVariant.Light) {
         Primary = ThemeColor.Parse("#0F6CBD");
         OnPrimary = ThemeColor.Parse("#FFFFFF");
         PrimaryContainer = ThemeColor.Parse("#D6E3FF");
         OnPrimaryContainer = ThemeColor.Parse("#001B3C");
         Secondary = ThemeColor.Parse("#00696B");
         OnSecondary = ThemeColor.Parse("#FFFFFF");
         SecondaryContainer = ThemeColor.Parse("#BEEBEB");
         OnSecondaryContainer = ThemeColor.Parse("#002020");
         Surface = ThemeColor.Parse("#FAF9FD");
         OnSurface = ThemeColor.Parse("#1A1C1E");
         OnSurfaceVariant = ThemeColor.Parse("#43474E");
         SurfaceContainerLow = ThemeColor.Parse("#F4F3F7");
         SurfaceContainer = ThemeColor.Parse("#EEEDF1");
         SurfaceContainerHigh = ThemeColor.Parse("#E8E7EC");
         Outline = ThemeColor.Parse("#73777F");
         OutlineVariant = ThemeColor.Parse("#C3C6CF");
         Error = ThemeColor.Parse("#BA1A1A");
         ErrorContainer = ThemeColor.Parse("#FFDAD6");
         OnErrorContainer = ThemeColor.Parse("#410002");
         Success = ThemeColor.Parse("#2FA84F");
         Warning = ThemeColor.Parse("#C98A00");
         Info = ThemeColor.Parse("#3B82F6");
         Scrim = ThemeColor.Parse("#000000");
         Brand = ThemeColor.Parse("#0F6CBD");
         OnBrand = ThemeColor.Parse("#FFFFFF");
      }
   }
}
