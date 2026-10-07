namespace Em.Ui.Maui.Controls
{
   /// <summary>
   /// A collection of Font Awesome icons in the form of glyph characters, ready to be attached to
   /// <see cref="FontIcon.Glyph"/>.
   /// </summary>
   /// <remarks>
   /// This client uses Font Awesome 7 Free, whose file is carried by this library and registered by it when
   /// the application is built. The values here are written as Unicode codepoints, not as the enum names of
   /// a package: the Font Awesome package used by the WPF client has no Android target, and even its
   /// framework-neutral part does not carry the codepoints. The codepoints of old icons are stable across
   /// major versions, so this table still matches Font Awesome 6 in the WPF client.
   /// </remarks>
   public static class FontIcons
   {
      /// <summary>The name of the font alias that holds all the glyphs in this class.</summary>
      public const string FontFamily = "FontAwesomeSolid";

      /// <summary>The default side length of an icon in device units.</summary>
      public const double DefaultSize = 24d;

      /// <summary>A left arrow, going back to the previous screen.</summary>
      public const string ArrowLeft = "";

      /// <summary>A right arrow, going forward to the next screen.</summary>
      public const string ArrowRight = "";

      /// <summary>A clockwise circular arrow, reloading the content of the screen.</summary>
      public const string RotateRight = "";

      /// <summary>A house, the home screen.</summary>
      public const string House = "";

      /// <summary>A magnifying glass, the mark of a search box.</summary>
      public const string MagnifyingGlass = "";

      /// <summary>A down arrow, the mark of a group that is open.</summary>
      public const string ChevronDown = "";

      /// <summary>A right arrow, the mark of a group that is closed.</summary>
      public const string ChevronRight = "";

      /// <summary>A gear, the mark of settings.</summary>
      public const string Gear = "";

      /// <summary>A plug, the mark of a connection to a server.</summary>
      public const string Plug = "";

      /// <summary>An arrow into a door, starting a session.</summary>
      public const string RightToBracket = "";

      /// <summary>An arrow out of a door, ending a session.</summary>
      public const string RightFromBracket = "";

      /// <summary>A key, the mark of a password.</summary>
      public const string Key = "\uf084";

      /// <summary>A check mark, the mark of a requirement that is met.</summary>
      public const string Check = "\uf00c";

      /// <summary>An envelope, the mark of an e-mail address.</summary>
      public const string Envelope = "";

      /// <summary>A cross, closing a panel.</summary>
      public const string Xmark = "";

      /// <summary>Two stacked bars, the mark of a server.</summary>
      public const string Server = "";

      /// <summary>A half-filled circle, the theme switch button.</summary>
      /// <remarks>
      /// One glyph for both directions, just like the desktop client: the button means "change theme", not "the
      /// theme currently active", so its picture need not change. What names the target theme is the text
      /// beside it, in a place that does have text.
      /// </remarks>
      public const string CircleHalfStroke = "\uf042";

      /// <summary>A crescent moon, the mark of the dark theme.</summary>
      public const string Moon = "";

      /// <summary>A sun, the mark of the light theme.</summary>
      public const string Sun = "";

      /// <summary>A person silhouette, the mark of a user.</summary>
      public const string User = "";

      /// <summary>An open padlock, the mark of a user who has not signed in.</summary>
      public const string LockOpen = "";

      /// <summary>Four large squares, the mark of a collection of modules.</summary>
      public const string TableCellsLarge = "";

      /// <summary>A window, the mark of a module screen.</summary>
      public const string WindowMaximize = "";
   }
}
