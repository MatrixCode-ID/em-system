using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Em.Ui.Wpf.Windows
{
   /// <summary>
   /// Window dasar aplikasi Em: window WPF biasa dengan baris judul buatan sendiri (ikon, judul,
   /// dan tombol minimize/maximize/close) yang memakai warna tema aktif, sehingga dialog dan window
   /// tambahan tampil satu bahasa dengan window utama di tema terang maupun gelap.
   /// </summary>
   /// <remarks>
   /// Tampilannya berasal dari default style di <c>Themes/Generic.xaml</c>, jadi turunannya cukup
   /// mengganti root XAML-nya menjadi <c>windows:EmWindow</c> dan mengisi kontennya seperti window
   /// biasa. Tombol caption mengikuti <see cref="Window.ResizeMode"/> dengan sendirinya:
   /// <c>NoResize</c> menyembunyikan minimize dan maximize, <c>CanMinimize</c> menyembunyikan
   /// maximize. Untuk dialog yang tetap boleh diubah ukurannya, matikan lewat
   /// <see cref="ShowMinimizeButton"/> dan <see cref="ShowMaximizeButton"/>.
   /// </remarks>
   public class EmWindow : Window
   {
      static EmWindow() {
         DefaultStyleKeyProperty.OverrideMetadata(
            typeof(EmWindow), new FrameworkPropertyMetadata(typeof(EmWindow)));
      }

      /// <summary>
      /// Membuat window dan menyambungkan tombol-tombol caption ke perintah sistem window.
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
      /// Menampilkan tombol minimize di baris judul. Bawaannya <c>true</c>; dialog biasanya
      /// mematikannya. Tetap tersembunyi kalau <see cref="Window.ResizeMode"/> bernilai
      /// <c>NoResize</c>.
      /// </summary>
      public bool ShowMinimizeButton {
         get => (bool)GetValue(ShowMinimizeButtonProperty);
         set => SetValue(ShowMinimizeButtonProperty, value);
      }

      /// <summary>
      /// Menampilkan tombol maximize/restore di baris judul. Bawaannya <c>true</c>; dialog biasanya
      /// mematikannya. Tetap tersembunyi kalau <see cref="Window.ResizeMode"/> bernilai
      /// <c>NoResize</c> atau <c>CanMinimize</c>.
      /// </summary>
      public bool ShowMaximizeButton {
         get => (bool)GetValue(ShowMaximizeButtonProperty);
         set => SetValue(ShowMaximizeButtonProperty, value);
      }

      /// <summary>
      /// Konten tambahan di baris judul, di antara judul dan tombol caption - mis. kolom pencarian
      /// atau tombol milik window itu sendiri. Kosong secara bawaan. Elemen di sini tetap bisa diklik
      /// walaupun berada di area baris judul yang dipakai untuk menggeser window.
      /// </summary>
      public object? TitleBarContent {
         get => GetValue(TitleBarContentProperty);
         set => SetValue(TitleBarContentProperty, value);
      }

      /// <inheritdoc />
      /// <remarks>
      /// Window yang mengukur dirinya dari isinya (<see cref="Window.SizeToContent"/>) diukur ulang
      /// sekali di sini, supaya tidak tersisa bidang hitam di kanan dan bawahnya. Dilakukan sebelum
      /// window pertama kali tampil, jadi user langsung melihat ukuran akhirnya.
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
      /// Tebal bingkai yang dipasang saat window maximized, supaya isinya tidak terpotong di tepi layar.
      /// Window tanpa title bar bawaan tetap diperbesar Windows melewati layar sejauh bingkai resize
      /// ditambah padded border; nilai ini menutup keduanya. Dipakai juga oleh window utama.
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
