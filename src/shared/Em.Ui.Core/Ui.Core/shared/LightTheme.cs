namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// The standard Em palette for light mode, derived from the brand color: blue <c>#0F6CBD</c> as the
   /// primary color and teal as the companion color. Used by <see cref="BrandingInfo.LightTheme"/> when the
   /// application does not install its own light theme.
   /// </summary>
   /// <remarks>
   /// This class is deliberately not <c>sealed</c>: an application may derive from it to replace some of
   /// the colors, or simply use an object initializer, e.g.
   /// <c>new LightTheme { Brand = ThemeColor.Parse("#7A1F2B") }</c>.
   /// </remarks>
   public class LightTheme : ThemeBase
   {
      /// <summary>
      /// Creates a light theme with the standard Em palette.
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
