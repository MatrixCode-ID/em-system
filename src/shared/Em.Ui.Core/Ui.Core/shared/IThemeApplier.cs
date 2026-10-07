namespace Em.Ui.Core.Shared
{
   /// <summary>
   /// An additional theme applier, for third-party control libraries that have their own theme system
   /// (e.g. a library a module uses for a grid or a special editor). The engine already applies the theme
   /// colors to its own controls; this applier translates the same theme into that library so all screens
   /// stay one color.
   /// </summary>
   /// <remarks>
   /// Registered through <c>AddThemeApplier&lt;T&gt;()</c> on the application builder. Every registered
   /// applier is called on the UI thread every time the active theme is applied - at startup and every
   /// time the user changes theme - after the engine has finished applying its own colors.
   /// </remarks>
   public interface IThemeApplier
   {
      /// <summary>
      /// Applies the currently active theme to the control library this applier serves.
      /// </summary>
      /// <param name="theme">The active theme; <see cref="ThemeBase.Variant"/> names its mode.</param>
      void Apply(ThemeBase theme);
   }
}
