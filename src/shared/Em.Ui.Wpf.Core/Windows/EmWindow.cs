using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// The base window of Em applications: an ordinary WPF window with a hand-made title row (icon, title,
   /// and minimize/maximize/close buttons) that uses the active theme colors, so dialogs and extra windows
   /// look like the same language as the main window in both light and dark themes.
   /// </summary>
   /// <remarks>
   /// Its look comes from the default style in <c>Themes/Generic.xaml</c>, so a derived class only needs to
   /// change its XAML root to <c>windows:EmWindow</c> and fill its content like an ordinary window. The
   /// caption buttons follow <see cref="Window.ResizeMode"/> by themselves: <c>NoResize</c> hides minimize
   /// and maximize, <c>CanMinimize</c> hides maximize. For a dialog that may still be resized, turn them
   /// off through <see cref="ShowMinimizeButton"/> and <see cref="ShowMaximizeButton"/>.
   /// </remarks>
   public class EmWindow : Window
   {
      static EmWindow() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EmWindow), new FrameworkPropertyMetadata(typeof(EmWindow)));
      }

      /// <summary>
      /// Creates the window and connects the caption buttons to the window system commands.
      /// </summary>
      public EmWindow() {
         // The caption buttons are part of this control's own template, not of any screen, so they
         // are wired here rather than through a view model.
         CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand,
            (_, _) => SystemCommands.MinimizeWindow(this)));
         CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand,
            (_, _) => SystemCommands.MaximizeWindow(this)));
         CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand,
            (_, _) => SystemCommands.RestoreWindow(this)));
         CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand,
            (_, _) => SystemCommands.CloseWindow(this)));
      }

      /// <summary>Mengidentifikasi property <see cref="ShowMinimizeButton"/>.</summary>
      public static readonly DependencyProperty ShowMinimizeButtonProperty =
         DependencyProperty.Register(
            nameof(ShowMinimizeButton), typeof(bool), typeof(EmWindow),
            new FrameworkPropertyMetadata(true));

      /// <summary>Mengidentifikasi property <see cref="ShowMaximizeButton"/>.</summary>
      public static readonly DependencyProperty ShowMaximizeButtonProperty =
         DependencyProperty.Register(
            nameof(ShowMaximizeButton), typeof(bool), typeof(EmWindow),
            new FrameworkPropertyMetadata(true));

      /// <summary>Mengidentifikasi property <see cref="TitleBarContent"/>.</summary>
      public static readonly DependencyProperty TitleBarContentProperty =
         DependencyProperty.Register(
            nameof(TitleBarContent), typeof(object), typeof(EmWindow),
            new FrameworkPropertyMetadata(null));

      /// <summary>
      /// Shows the minimize button in the title row. The default is <c>true</c>; dialogs usually turn it off.
      /// It stays hidden when <see cref="Window.ResizeMode"/> is <c>NoResize</c>.
      /// </summary>
      public bool ShowMinimizeButton {
         get => (bool)GetValue(ShowMinimizeButtonProperty);
         set => SetValue(ShowMinimizeButtonProperty, value);
      }

      /// <summary>
      /// Shows the maximize/restore button in the title row. The default is <c>true</c>; dialogs usually turn
      /// it off. It stays hidden when <see cref="Window.ResizeMode"/> is <c>NoResize</c> or <c>CanMinimize</c>.
      /// </summary>
      public bool ShowMaximizeButton {
         get => (bool)GetValue(ShowMaximizeButtonProperty);
         set => SetValue(ShowMaximizeButtonProperty, value);
      }

      /// <summary>
      /// Extra content in the title row, between the title and the caption buttons - e.g. a search box or a
      /// button belonging to that window itself. Empty by default. An element here can still be clicked even
      /// though it sits in the part of the title row used to drag the window.
      /// </summary>
      public object? TitleBarContent {
         get => GetValue(TitleBarContentProperty);
         set => SetValue(TitleBarContentProperty, value);
      }

      /// <inheritdoc />
      /// <remarks>
      /// A window that measures itself from its content (<see cref="Window.SizeToContent"/>) is measured again
      /// once here, so no black area is left on its right and bottom. It is done before the window is first
      /// shown, so the user immediately sees the final size.
      /// </remarks>
      protected override void OnSourceInitialized(EventArgs e) {
         base.OnSourceInitialized(e);
         // A dialog wears the application's icon, the one the main window has, unless it was given its own.
         if (ReadLocalValue(IconProperty) == DependencyProperty.UnsetValue &&
             Application.Current?.MainWindow is TabbedMainWindow { Icon: { } appIcon }) Icon = appIcon;
         FitToContentAfterChrome();
      }

      // WPF sizes a SizeToContent window as its content plus the standard window frame, and only
      // then does WindowChrome take that frame away - the window keeps the larger size, and the part
      // nothing draws on shows black along the right and bottom edges. Measuring again once the
      // chrome is in place gives the true size; the window is shifted by half of what it lost so it
      // stays centred where it was placed.
      //
      // SourceInitialized is the earliest point the chrome is in place, and the window is not visible
      // yet. Doing this after the first render instead (ContentRendered) left one frame on screen at
      // the larger size - the dialog visibly shrank and jumped as it opened.
      private void FitToContentAfterChrome() {
         if (SizeToContent == SizeToContent.Manual || WindowState != WindowState.Normal) return;

         var width = ActualWidth;
         var height = ActualHeight;
         var mode = SizeToContent;
         SizeToContent = SizeToContent.Manual;
         SizeToContent = mode;
         UpdateLayout();

         Left += (width - ActualWidth) / 2;
         Top += (height - ActualHeight) / 2;
      }

      /// <summary>
      /// The border thickness applied when the window is maximized, so its content is not cut off at the edge
      /// of the screen. A window without the default title bar is still enlarged by Windows past the screen by
      /// the resize frame plus the padded border; this value covers both. Also used by the main window.
      /// </summary>
      public static Thickness MaximizedBorderThickness { get; } = ComputeMaximizedBorderThickness();

      // SystemParameters.WindowResizeBorderThickness covers the sizing frame only. The padded border
      // Windows adds around every sizable window is left out of it, and a maximized window hangs that
      // much further off each edge - 4px at 100%, which is what cut the top of the title bar.
      private static Thickness ComputeMaximizedBorderThickness() {
         const int SM_CXPADDEDBORDER = 92;
         var padded = GetSystemMetrics(SM_CXPADDEDBORDER) * 96.0 / GetDpiForSystem();
         var frame = SystemParameters.WindowResizeBorderThickness;
         return new Thickness(frame.Left + padded, frame.Top + padded, frame.Right + padded, frame.Bottom + padded);
      }

      [DllImport("user32.dll")]
      private static extern int GetSystemMetrics(int index);

      [DllImport("user32.dll")]
      private static extern uint GetDpiForSystem();
   }
}
