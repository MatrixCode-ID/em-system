namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Light or dark mode. <see cref="BrandingInfo"/> always carries one theme for each mode, and the mode
   /// chosen by the user decides which theme is in use.
   /// </summary>
   public enum ThemeVariant
   {
      /// <summary>
      /// Mode terang: bidang berwarna muda, teks berwarna gelap.
      /// </summary>
      Light,

      /// <summary>
      /// Mode gelap: bidang berwarna gelap, teks berwarna muda.
      /// </summary>
      Dark
   }
}
