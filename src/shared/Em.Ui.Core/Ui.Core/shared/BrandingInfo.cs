namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// Settings for the application's brand display: the logo, the texts on the login screen's branding
   /// panel, and the light/dark themes used across the application. Installed through
   /// <c>EmAppBuilder.ApplyBranding</c>; all properties are optional and fall back to generic defaults
   /// when left empty.
   /// </summary>
   /// <remarks>
   /// This class is only data and is the same for all clients. How the logo is loaded and how theme colors
   /// are translated is the business of each platform's core.
   /// </remarks>
   public sealed class BrandingInfo
   {
      // Generic defaults are used when the matching property below is left empty. Deliberately they name no
      // particular company so they make sense for any application that never called
      // EmAppBuilder.ApplyBranding at all.
      private const string DefaultTitle = "Your Company";
      private const string DefaultTagline = "Enterprise Application";

      private const string DefaultDescriptionText =
         "One workspace for your operations, assets, and people — connected end to end.";

      private ThemeBase _lightTheme = new LightTheme();
      private ThemeBase _darkTheme = new DarkTheme();

      /// <summary>
      /// Location of the application logo. How it is interpreted is decided by the platform core that shows it:
      /// <list type="bullet">
      /// <item>WPF: a pack URI to a resource of the application project, e.g.
      /// <c>"pack://application:,,,/Em.Ui.Wpf;component/Logo.png"</c>. The WPF core only loads bitmaps
      /// (<c>.png</c>, <c>.jpg</c>, <c>.ico</c>, <c>.bmp</c>); other formats (e.g. <c>.svg</c>) need an
      /// additional logo loader installed by the application.</item>
      /// <item>MAUI: the name of an image file in the application project's <c>Resources/Images</c>,
      /// lowercase, with the <c>.png</c> extension also for <c>.svg</c> files (MAUI converts it to a bitmap at
      /// build time).</item>
      /// </list>
      /// When left empty or when it fails to load, the platform core's default logo is used.
      /// </summary>
      public string? LogoSource { get; set; }

      /// <summary>Choice of WPF login screen; the default is Material. Not used by MAUI yet.</summary>
      public LoginStyle LoginStyle { get; set; } = LoginStyle.Material;

      /// <summary>
      /// Light background, only for WPF with LoginStyle.Material; ignored by Classic and MAUI.
      /// Interpreted the same as LogoSource: a pack URI or an absolute URI, a bitmap unless an ILogoImageLoader exists.
      /// A width of about 1920 px is recommended. If only one image is filled in, it is used in both modes;
      /// dark mode dims a borrowed light image. A failure to load falls back to the other mode, then to a gradient.
      /// </summary>
      public string? LightLoginBackground { get; set; }

      /// <summary>
      /// Dark background, only for WPF with LoginStyle.Material; ignored by Classic and MAUI.
      /// Interpreted the same as LogoSource: a pack URI or an absolute URI, a bitmap unless an ILogoImageLoader exists.
      /// A width of about 1920 px is recommended. If only one image is filled in, it is used in both modes;
      /// dark mode dims a borrowed light image. A failure to load falls back to the other mode, then to a gradient.
      /// </summary>
      public string? DarkLoginBackground { get; set; }

      /// <summary>
      /// Location of the application icon for windows (the icon on the taskbar and in the title bar) and the
      /// small logo in the main window's title bar. Used by WPF only: a pack URI to an <c>.ico</c> file (or
      /// bitmap) of the application project, e.g. <c>"pack://application:,,,/Em.Ui.Wpf;component/Logo.ico"</c>.
      /// The icon of the <c>.exe</c> file in Explorer does not change with it; that is set by
      /// <c>ApplicationIcon</c> in the application project. When left empty or when it fails to load, the
      /// core's default icon is used.
      /// </summary>
      public string? IconSource { get; set; }

      /// <summary>
      /// Brand title on the login panel (e.g. <c>"EM"</c>), shown large below the logo. When left empty,
      /// <c>"Your Company"</c> is used.
      /// </summary>
      public string? Title { get; set; }

      /// <summary>
      /// Brand subtitle on the login panel (e.g. <c>"Corporate Service Management"</c>), shown right below
      /// <see cref="Title"/>. When left empty, <c>"Enterprise Application"</c> is used.
      /// </summary>
      public string? Tagline { get; set; }

      /// <summary>
      /// Short description paragraph on the login panel, below the divider line. When left empty, a generic
      /// sentence that names no particular company is used.
      /// </summary>
      public string? Description { get; set; }

      /// <summary>
      /// Copyright text in the footer of the login panel (e.g. <c>"© 2026 EM. All rights reserved."</c>). When
      /// left empty, <c>"© {current year} Your Company. All rights reserved."</c> is used.
      /// </summary>
      public string? Copyright { get; set; }

      /// <summary>
      /// Theme for light mode. The default is <see cref="Shared.LightTheme"/> (the standard Em palette).
      /// </summary>
      /// <exception cref="ArgumentException">The theme that was set is not a light mode theme.</exception>
      public ThemeBase LightTheme {
         get => _lightTheme;
         set => _lightTheme = Validate(value, ThemeVariant.Light);
      }

      /// <summary>
      /// Theme for dark mode. The default is <see cref="Shared.DarkTheme"/> (the standard Em palette).
      /// </summary>
      /// <exception cref="ArgumentException">The theme that was set is not a dark mode theme.</exception>
      public ThemeBase DarkTheme {
         get => _darkTheme;
         set => _darkTheme = Validate(value, ThemeVariant.Dark);
      }

      /// <summary>
      /// The brand title that is actually shown on the login panel: <see cref="Title"/> when filled in, or
      /// <c>"Your Company"</c> when not.
      /// </summary>
      public string DisplayTitle => Title ?? DefaultTitle;

      /// <summary>
      /// The brand subtitle that is actually shown: <see cref="Tagline"/> when filled in, or
      /// <c>"Enterprise Application"</c> when not.
      /// </summary>
      public string DisplayTagline => Tagline ?? DefaultTagline;

      /// <summary>
      /// The description paragraph that is actually shown: <see cref="Description"/> when filled in, or the
      /// default generic sentence when not.
      /// </summary>
      public string DisplayDescription => Description ?? DefaultDescriptionText;

      /// <summary>
      /// The copyright text that is actually shown: <see cref="Copyright"/> when filled in, or
      /// <c>"© {current year} Your Company. All rights reserved."</c> when not.
      /// </summary>
      public string DisplayCopyright => Copyright ?? $"© {DateTime.Now.Year} {DefaultTitle}. All rights reserved.";

      /// <summary>
      /// The theme for the requested mode: <see cref="LightTheme"/> or <see cref="DarkTheme"/>.
      /// </summary>
      public ThemeBase GetTheme(ThemeVariant variant) => variant == ThemeVariant.Light ? LightTheme : DarkTheme;

      private static ThemeBase Validate(ThemeBase theme, ThemeVariant expected) {
         ArgumentNullException.ThrowIfNull(theme);
         if (theme.Variant != expected)
            throw new ArgumentException($"A {theme.Variant} theme cannot be used as the {expected} theme.", nameof(theme));
         return theme;
      }
   }
}
