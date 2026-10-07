namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Light or dark mode. <see cref="BrandingInfo"/> always carries one theme for each mode, and the mode
   /// chosen by the user decides which theme is in use.
   /// </summary>
   public enum ThemeVariant
   {
      /// <summary>
      /// Light mode: light-colored surfaces, dark text.
      /// </summary>
      Light,

      /// <summary>
      /// Dark mode: dark-colored surfaces, light text.
      /// </summary>
      Dark
   }
}
