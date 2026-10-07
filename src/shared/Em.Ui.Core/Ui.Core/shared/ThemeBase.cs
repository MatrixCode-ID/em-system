namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// One set of theme colors for one mode (light or dark), following the Material 3 color roles. Screens
   /// and controls name the role, not the color code, so changing the theme only requires swapping this
   /// object. The <c>On...</c> pairs are the text/icon colors that read well on top of their paired role,
   /// e.g. <see cref="OnPrimary"/> on <see cref="Primary"/>.
   /// </summary>
   /// <remarks>
   /// This object is only data - it applies nothing by itself. Each client (and the control library used
   /// by modules, through <see cref="IThemeApplier"/>) reads this theme and translates it into its own
   /// display system.
   /// <para>
   /// The standard Em palettes are <see cref="LightTheme"/> and <see cref="DarkTheme"/>. An application
   /// that needs other colors only needs to derive from one of them, or replace some roles through an
   /// object initializer, e.g. <c>new LightTheme { Brand = ThemeColor.Parse("#7A1F2B") }</c>, then attach
   /// it to <see cref="BrandingInfo"/>.
   /// </para>
   /// </remarks>
   public abstract class ThemeBase
   {
      /// <summary>
      /// Creates a theme for the given mode. All color roles are still empty (transparent) until filled by a
      /// derived class or an object initializer.
      /// </summary>
      /// <param name="variant">The mode this theme serves.</param>
      protected ThemeBase(ThemeVariant variant) {
         Variant = variant;
      }

      /// <summary>
      /// The mode this theme serves: light or dark.
      /// </summary>
      public ThemeVariant Variant { get; }

      #region Primary

      /// <summary>
      /// Primary color: the single most important action on a screen, and the marker of the active choice.
      /// </summary>
      public ThemeColor Primary { get; init; }

      /// <summary>
      /// Text/icon on top of <see cref="Primary"/>.
      /// </summary>
      public ThemeColor OnPrimary { get; init; }

      /// <summary>
      /// A calmer surface tinted with the primary color, e.g. the background of a selected item.
      /// </summary>
      public ThemeColor PrimaryContainer { get; init; }

      /// <summary>
      /// Text/icon on top of <see cref="PrimaryContainer"/>.
      /// </summary>
      public ThemeColor OnPrimaryContainer { get; init; }

      #endregion

      #region Secondary

      /// <summary>
      /// Companion color: a marker that accompanies the primary color, e.g. the selected area in the side menu.
      /// </summary>
      public ThemeColor Secondary { get; init; }

      /// <summary>
      /// Text/icon on top of <see cref="Secondary"/>.
      /// </summary>
      public ThemeColor OnSecondary { get; init; }

      /// <summary>
      /// A calmer surface tinted with the companion color.
      /// </summary>
      public ThemeColor SecondaryContainer { get; init; }

      /// <summary>
      /// Text/icon on top of <see cref="SecondaryContainer"/>.
      /// </summary>
      public ThemeColor OnSecondaryContainer { get; init; }

      #endregion

      #region Surface

      /// <summary>
      /// Background of pages and windows.
      /// </summary>
      public ThemeColor Surface { get; init; }

      /// <summary>
      /// Main text/icon on top of all surfaces.
      /// </summary>
      public ThemeColor OnSurface { get; init; }

      /// <summary>
      /// Dimmer secondary text/icon, e.g. labels and captions.
      /// </summary>
      public ThemeColor OnSurfaceVariant { get; init; }

      /// <summary>
      /// The lowest layered surface on top of <see cref="Surface"/>.
      /// </summary>
      public ThemeColor SurfaceContainerLow { get; init; }

      /// <summary>
      /// Bidang bertingkat standar, mis. kartu.
      /// </summary>
      public ThemeColor SurfaceContainer { get; init; }

      /// <summary>
      /// The highest layered surface, e.g. input fields, strips inside a card, or popups.
      /// </summary>
      public ThemeColor SurfaceContainerHigh { get; init; }

      #endregion

      #region Outline

      /// <summary>
      /// Border line of cards and input fields.
      /// </summary>
      public ThemeColor Outline { get; init; }

      /// <summary>
      /// A calmer divider line, e.g. between rows.
      /// </summary>
      public ThemeColor OutlineVariant { get; init; }

      #endregion

      #region Status

      /// <summary>
      /// Errors and destructive/dangerous actions.
      /// </summary>
      public ThemeColor Error { get; init; }

      /// <summary>
      /// A surface tinted with the error color, e.g. the background of an error message.
      /// </summary>
      public ThemeColor ErrorContainer { get; init; }

      /// <summary>
      /// Text/icon on top of <see cref="ErrorContainer"/>.
      /// </summary>
      public ThemeColor OnErrorContainer { get; init; }

      /// <summary>
      /// Success/active status.
      /// </summary>
      public ThemeColor Success { get; init; }

      /// <summary>
      /// A status that needs attention but is not an error.
      /// </summary>
      public ThemeColor Warning { get; init; }

      /// <summary>
      /// A neutral informative status.
      /// </summary>
      public ThemeColor Info { get; init; }

      #endregion

      #region Others

      /// <summary>
      /// The curtain behind a panel or dialog that is currently open.
      /// </summary>
      public ThemeColor Scrim { get; init; }

      /// <summary>
      /// Color of the brand panel, e.g. the branding panel on the login screen and the header of the side menu.
      /// </summary>
      public ThemeColor Brand { get; init; }

      /// <summary>
      /// Text/icon and decoration on top of <see cref="Brand"/>. On the login screen this color is also used
      /// for faint decoration on the branding panel, so it may appear thin, not as a solid surface.
      /// </summary>
      public ThemeColor OnBrand { get; init; }

      #endregion
   }
}
