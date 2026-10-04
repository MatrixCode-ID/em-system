namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Mode terang atau gelap. <see cref="BrandingInfo"/> selalu membawa satu tema untuk masing-masing
   /// mode, dan mode yang dipilih pengguna inilah yang menentukan tema mana yang sedang dipakai.
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
